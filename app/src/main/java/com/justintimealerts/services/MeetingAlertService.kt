package com.justintimealerts.services

import com.justintimealerts.models.MeetingEvent
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.Duration
import java.time.Instant

/**
 * Core logic engine that decides which meetings need an alert right now.
 * This is platform-agnostic; Android-specific notification triggering is
 * handled by the platform service layer.
 */
class MeetingAlertService(
    private val parser: IcsParserService,
    private val repository: CalendarSourceRepository,
    private val cache: ProcessedMeetingCache,
    private val log: DebugLogService
) {
    companion object {
        /**
         * The "just-in-time" window: a meeting is considered starting "right now"
         * if its start time falls within the last [ALERT_WINDOW].
         */
        val ALERT_WINDOW: Duration = Duration.ofMinutes(1)
    }
    
    private val checkLock = Mutex()
    
    /** Listeners called when a meeting is starting. */
    val meetingStartingListeners = mutableListOf<(MeetingEvent) -> Unit>()
    
    /**
     * Checks all active calendar sources for meetings that are starting right
     * now and notifies listeners for each unalerted meeting.
     */
    suspend fun checkAndAlert() {
        // Skip rather than queue: if a check is already in progress, discard the duplicate
        if (!checkLock.tryLock()) {
            log.log("CheckAndAlert: skipped (already running).")
            return
        }
        
        try {
            val now = Instant.now()
            val windowStart = now.minus(ALERT_WINDOW)
            
            // Evict stale UIDs from the cache on each check cycle.
            cache.evict()
            
            val activeSources = repository.sources.filter { it.isActive }
            log.log("CheckAndAlert: ${activeSources.size} active source(s). Window: ${windowStart} – $now UTC")
            
            for (source in activeSources) {
                val events = parser.getEvents(source)
                repository.updateLastSync(source.id)
                
                for (meeting in events) {
                    // Meeting is "starting right now" if its start time is within the alert window.
                    if (meeting.start >= windowStart && meeting.start <= now) {
                        if (cache.shouldAlert(meeting.uid)) {
                            log.log("ALERT: \"${meeting.title}\" at ${meeting.start}")
                            cache.markAlerted(meeting.uid, meeting.end)
                            notifyMeetingStarting(meeting)
                        } else {
                            log.log("Skipped (already alerted): \"${meeting.title}\"")
                        }
                    }
                }
            }
        } finally {
            checkLock.unlock()
        }
    }
    
    private fun notifyMeetingStarting(meeting: MeetingEvent) {
        meetingStartingListeners.forEach { it(meeting) }
    }
}
