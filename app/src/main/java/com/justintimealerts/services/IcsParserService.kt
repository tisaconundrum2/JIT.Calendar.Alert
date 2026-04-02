package com.justintimealerts.services

import com.justintimealerts.models.CalendarSource
import com.justintimealerts.models.MeetingEvent
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import net.fortuna.ical4j.data.CalendarBuilder
import net.fortuna.ical4j.model.Calendar
import net.fortuna.ical4j.model.Component
import net.fortuna.ical4j.model.Property
import net.fortuna.ical4j.model.component.VEvent
import net.fortuna.ical4j.model.property.DtEnd
import net.fortuna.ical4j.model.property.DtStart
import net.fortuna.ical4j.model.property.RRule
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.StringReader
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant
import java.time.temporal.Temporal
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

/**
 * Fetches and parses ICS calendars from a URL or a local file path,
 * returning strongly-typed [MeetingEvent] objects.
 *
 * Network-efficiency strategy (prevents tight-loop DOS behaviour):
 * - URLs are re-fetched at most once every [MIN_FETCH_INTERVAL_MS] (15 min).
 * - Every URL request sends If-None-Match / If-Modified-Since headers.
 *   A 304 Not Modified response returns the in-memory cache instantly.
 * - Even on a 200 response the ICS is only re-parsed when the SHA-256
 *   content hash differs from the previously cached value.
 * - Consecutive fetch failures trigger exponential back-off (1 min → 5 min → 15 min)
 *   so a broken feed never produces an infinite retry loop.
 */
class IcsParserService(
    private val httpClient: OkHttpClient,
    private val log: DebugLogService
) {
    companion object {
        /** Minimum time between actual HTTP fetches for the same URL. */
        const val MIN_FETCH_INTERVAL_MS: Long = 15 * 60 * 1000L // 15 minutes
        
        private val BACKOFF_STEPS = longArrayOf(
            1 * 60 * 1000L,  // 1 minute
            5 * 60 * 1000L,  // 5 minutes
            15 * 60 * 1000L  // 15 minutes
        )
    }
    
    private data class SourceCache(
        var eTag: String? = null,
        var lastModified: String? = null,
        var contentHash: String? = null,
        var events: List<MeetingEvent> = emptyList(),
        var lastFetchTime: Long = 0L,
        var consecutiveFailures: Int = 0,
        var backoffUntil: Long = 0L,
        var fileLastWrite: Long = 0L
    )
    
    private val cache = ConcurrentHashMap<String, SourceCache>()
    
    /**
     * Wipes all in-memory cache entries (or a single entry keyed by [cacheKey])
     * so that the next [getEvents] call performs an unconditional HTTP fetch
     * and full re-parse.
     */
    fun invalidateCache(cacheKey: String? = null) {
        if (cacheKey == null) {
            cache.values.forEach { resetEntry(it) }
            log.log("ICS cache: all ${cache.size} source(s) invalidated for force-sync.")
        } else {
            cache[cacheKey]?.let {
                resetEntry(it)
                log.log("ICS cache: '$cacheKey' invalidated for force-sync.")
            }
        }
    }
    
    private fun resetEntry(entry: SourceCache) {
        entry.lastFetchTime = 0L
        entry.backoffUntil = 0L
        entry.contentHash = null
        entry.eTag = null
        entry.lastModified = null
        entry.fileLastWrite = 0L
        entry.consecutiveFailures = 0
    }
    
    /**
     * Loads events from the given [source].
     * Returns cached events when no network fetch is needed.
     * Returns an empty list on unrecoverable failure so callers stay clean.
     */
    suspend fun getEvents(source: CalendarSource): List<MeetingEvent> = withContext(Dispatchers.IO) {
        when {
            !source.url.isNullOrBlank() -> fetchFromUrl(source)
            !source.filePath.isNullOrBlank() && File(source.filePath).exists() -> readFromFile(source)
            else -> {
                log.log("ICS source has no URL or valid file path — skipping.")
                emptyList()
            }
        }
    }
    
    private suspend fun fetchFromUrl(source: CalendarSource): List<MeetingEvent> = withContext(Dispatchers.IO) {
        val cacheKey = source.url!!
        val entry = cache.getOrPut(cacheKey) { SourceCache() }
        val now = System.currentTimeMillis()
        
        // Back-off guard
        if (now < entry.backoffUntil) {
            log.log("ICS [${source.displayName}]: in back-off until ${entry.backoffUntil} — returning cache.")
            return@withContext entry.events
        }
        
        // Minimum re-fetch interval
        if (entry.events.isNotEmpty() && (now - entry.lastFetchTime) < MIN_FETCH_INTERVAL_MS) {
            val minAgo = (now - entry.lastFetchTime) / 60_000.0
            log.log("ICS [${source.displayName}]: cache fresh (last fetch ${"%.1f".format(minAgo)} min ago) — skipping HTTP.")
            return@withContext entry.events
        }
        
        // Build conditional request
        val requestBuilder = Request.Builder().url(source.url)
        entry.eTag?.let { requestBuilder.addHeader("If-None-Match", it) }
        entry.lastModified?.let { requestBuilder.addHeader("If-Modified-Since", it) }
        
        log.log("ICS [${source.displayName}]: HTTP GET (ETag: ${entry.eTag ?: "none"})")
        
        try {
            val response = httpClient.newCall(requestBuilder.build()).execute()
            response.use { resp ->
                // 304 Not Modified
                if (resp.code == 304) {
                    entry.consecutiveFailures = 0
                    entry.lastFetchTime = now
                    log.log("ICS [${source.displayName}]: 304 Not Modified — using ${entry.events.size} cached event(s).")
                    return@withContext entry.events
                }
                
                // Non-success
                if (!resp.isSuccessful) {
                    return@withContext handleFetchFailure(
                        entry, source.displayName,
                        Exception("HTTP ${resp.code} ${resp.message}")
                    )
                }
                
                val icsContent = resp.body?.string() ?: ""
                
                // Content-hash dedup (skip re-parse if identical)
                val hash = computeHash(icsContent)
                if (hash == entry.contentHash) {
                    entry.consecutiveFailures = 0
                    entry.lastFetchTime = now
                    captureResponseHeaders(resp, entry)
                    log.log("ICS [${source.displayName}]: 200 but content unchanged — using ${entry.events.size} cached event(s).")
                    return@withContext entry.events
                }
                
                // Parse new content
                log.log("ICS [${source.displayName}]: new content (${icsContent.length} chars) — parsing.")
                val parsed = parseIcsContent(icsContent, source.id)
                log.log("ICS [${source.displayName}]: parsed ${parsed.size} event(s).")
                
                entry.contentHash = hash
                entry.events = parsed
                entry.lastFetchTime = now
                entry.consecutiveFailures = 0
                entry.backoffUntil = 0L
                captureResponseHeaders(resp, entry)
                
                return@withContext parsed
            }
        } catch (ex: Exception) {
            return@withContext handleFetchFailure(entry, source.displayName, ex)
        }
    }
    
    private fun readFromFile(source: CalendarSource): List<MeetingEvent> {
        val cacheKey = source.filePath!!
        val entry = cache.getOrPut(cacheKey) { SourceCache() }
        
        val file = File(source.filePath)
        val lastWrite = file.lastModified()
        
        if (entry.events.isNotEmpty() && lastWrite == entry.fileLastWrite) {
            log.log("ICS [${source.displayName}]: file unchanged — using ${entry.events.size} cached event(s).")
            return entry.events
        }
        
        return try {
            log.log("ICS [${source.displayName}]: reading file.")
            val icsContent = file.readText()
            val parsed = parseIcsContent(icsContent, source.id)
            log.log("ICS [${source.displayName}]: parsed ${parsed.size} event(s) from file.")
            
            entry.events = parsed
            entry.fileLastWrite = lastWrite
            entry.contentHash = computeHash(icsContent)
            
            parsed
        } catch (ex: Exception) {
            log.log("ERROR reading ICS file: ${ex.javaClass.simpleName}: ${ex.message}")
            entry.events // return stale cache rather than empty
        }
    }
    
    private fun handleFetchFailure(entry: SourceCache, displayName: String, ex: Exception): List<MeetingEvent> {
        entry.consecutiveFailures++
        val stepIdx = minOf(entry.consecutiveFailures - 1, BACKOFF_STEPS.size - 1)
        val backoff = BACKOFF_STEPS[stepIdx]
        entry.backoffUntil = System.currentTimeMillis() + backoff
        
        log.log("ERROR fetching ICS [$displayName] (failure #${entry.consecutiveFailures}): " +
                "${ex.javaClass.simpleName}: ${ex.message}. " +
                "Back-off for ${backoff / 60_000} min.")
        
        return entry.events // return stale cache rather than empty
    }
    
    private fun captureResponseHeaders(response: okhttp3.Response, entry: SourceCache) {
        response.header("ETag")?.let { entry.eTag = it }
        response.header("Last-Modified")?.let { entry.lastModified = it }
    }
    
    private fun computeHash(content: String): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val hashBytes = digest.digest(content.toByteArray())
        return hashBytes.joinToString("") { "%02x".format(it) }
    }
    
    /**
     * Sanitizes RRULE lines where both COUNT and UNTIL are present.
     * RFC 5545 §3.3.10 forbids supplying both; real-world feeds do it anyway.
     * We keep UNTIL and drop COUNT.
     */
    private fun sanitizeIcsContent(icsContent: String): String {
        // Unfold RFC 5545 line continuations
        var unfolded = icsContent.replace(Regex("\\r?\\n[ \\t]"), "")
        
        // Remove COUNT from RRULE lines that have both UNTIL and COUNT
        unfolded = unfolded.replace(
            Regex("(?im)^(RRULE[^:]*:[^\\r\\n]*UNTIL=[^\\r\\n]*)(?:;COUNT=\\d+|COUNT=\\d+;?)([^\\r\\n]*)\$"),
            "$1$2"
        )
        unfolded = unfolded.replace(
            Regex("(?im)^(RRULE[^:]*:(?:[^\\r\\n]*?))COUNT=\\d+;([^\\r\\n]*UNTIL=[^\\r\\n]*)\$"),
            "$1$2"
        )
        
        return unfolded
    }
    
    /**
     * Parses raw ICS text and returns a list of [MeetingEvent].
     */
    fun parseIcsContent(icsContent: String, calendarSourceId: String): List<MeetingEvent> {
        if (icsContent.isBlank()) return emptyList()
        
        return try {
            val sanitized = sanitizeIcsContent(icsContent)
            val builder = CalendarBuilder()
            val calendar: Calendar = builder.build(StringReader(sanitized))
            val events = mutableListOf<MeetingEvent>()
            
            for (component in calendar.getComponents<VEvent>(Component.VEVENT)) {
                val dtStart = component.getProperty<DtStart>(Property.DTSTART) ?: continue
                
                val uidProperty = component.getProperty<net.fortuna.ical4j.model.property.Uid>(Property.UID)
                val uid = uidProperty?.value?.takeIf { it.isNotBlank() } ?: UUID.randomUUID().toString()
                
                val summary = component.getProperty<net.fortuna.ical4j.model.property.Summary>(Property.SUMMARY)?.value ?: "(No Title)"
                val description = component.getProperty<net.fortuna.ical4j.model.property.Description>(Property.DESCRIPTION)?.value
                val location = component.getProperty<net.fortuna.ical4j.model.property.Location>(Property.LOCATION)?.value
                
                val startInstant = dateToInstant(dtStart.date)
                val dtEnd = component.getProperty<DtEnd>(Property.DTEND)
                val endInstant = if (dtEnd != null) {
                    dateToInstant(dtEnd.date)
                } else {
                    startInstant.plus(Duration.ofHours(1))
                }
                
                // Check for recurring events
                val rrule = component.getProperty<RRule>(Property.RRULE)
                if (rrule != null) {
                    // For recurring events, expand occurrences over the next 30 days
                    val rangeStart = Instant.now()
                    val rangeEnd = rangeStart.plus(Duration.ofDays(30))
                    
                    try {
                        val recur = rrule.recur
                        val seed = dtStart.date
                        val period = net.fortuna.ical4j.model.Period(
                            net.fortuna.ical4j.model.DateTime(java.util.Date.from(rangeStart)),
                            net.fortuna.ical4j.model.DateTime(java.util.Date.from(rangeEnd))
                        )
                        val occurrences = component.calculateRecurrenceSet(period)
                        
                        for (occurrence in occurrences) {
                            val occStart = Instant.ofEpochMilli(occurrence.start.time)
                            val occEnd = Instant.ofEpochMilli(occurrence.end.time)
                            val occUid = "${uid}_${occStart.epochSecond}"
                            
                            events.add(MeetingEvent(
                                uid = occUid,
                                title = summary,
                                start = occStart,
                                end = occEnd,
                                description = description,
                                location = location,
                                calendarSourceId = calendarSourceId
                            ))
                        }
                    } catch (e: Exception) {
                        // If recurrence expansion fails, add the base event
                        log.log("Warning: Failed to expand recurrence for $uid: ${e.message}")
                        events.add(MeetingEvent(
                            uid = uid,
                            title = summary,
                            start = startInstant,
                            end = endInstant,
                            description = description,
                            location = location,
                            calendarSourceId = calendarSourceId
                        ))
                    }
                } else {
                    events.add(MeetingEvent(
                        uid = uid,
                        title = summary,
                        start = startInstant,
                        end = endInstant,
                        description = description,
                        location = location,
                        calendarSourceId = calendarSourceId
                    ))
                }
            }
            
            events
        } catch (ex: Exception) {
            log.log("ERROR parsing ICS content: ${ex.javaClass.simpleName}: ${ex.message}")
            emptyList()
        }
    }
    
    private fun dateToInstant(date: java.util.Date): Instant {
        return when (date) {
            is net.fortuna.ical4j.model.DateTime -> Instant.ofEpochMilli(date.time)
            is net.fortuna.ical4j.model.Date -> Instant.ofEpochMilli(date.time)
            else -> date.toInstant()
        }
    }
}
