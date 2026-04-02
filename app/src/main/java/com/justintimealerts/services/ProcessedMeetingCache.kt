package com.justintimealerts.services

import java.time.Instant
import java.util.concurrent.ConcurrentHashMap

/**
 * Thread-safe cache that tracks which meeting UIDs have already triggered an alert.
 * Uses a ConcurrentHashMap mapping UIDs to their meeting end times, enabling automatic
 * eviction of entries after the meeting has been over for more than [expiryAfterEndMs].
 */
class ProcessedMeetingCache(
    /** Time after a meeting ends before its UID is evicted from the cache (default: 24 hours). */
    private val expiryAfterEndMs: Long = DEFAULT_EXPIRY_MS
) {
    companion object {
        /** Default time after a meeting ends before its UID is evicted from the cache. */
        const val DEFAULT_EXPIRY_MS: Long = 24 * 60 * 60 * 1000L // 24 hours
    }
    
    private val alerted = ConcurrentHashMap<String, Long>()
    
    /** Returns true if the UID has not yet been recorded as alerted. */
    fun shouldAlert(uid: String): Boolean = !contains(uid)
    
    /** Returns true if the cache contains the specified UID. */
    fun contains(uid: String): Boolean = alerted.containsKey(uid)
    
    /**
     * Records a UID as alerted, storing the meeting's [endTime]
     * so the entry can be evicted once the meeting has been over long enough.
     */
    fun markAlerted(uid: String, endTime: Instant) {
        alerted[uid] = endTime.toEpochMilli()
    }
    
    /** Removes all entries whose meetings ended more than [expiryAfterEndMs] ago. */
    fun evict() {
        val cutoff = System.currentTimeMillis() - expiryAfterEndMs
        alerted.entries.removeIf { it.value < cutoff }
    }
    
    /** Returns the number of UIDs currently in the cache (for diagnostics). */
    val count: Int
        get() = alerted.size
}
