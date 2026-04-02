package com.justintimealerts.models

import java.time.Instant

/**
 * Simplified representation of a calendar event derived from an ICS file.
 */
data class MeetingEvent(
    /** Unique identifier from the ICS UID field. */
    val uid: String,
    val title: String,
    val start: Instant,
    val end: Instant,
    val description: String? = null,
    val location: String? = null,
    /** Source calendar that provided this event. */
    val calendarSourceId: String
)
