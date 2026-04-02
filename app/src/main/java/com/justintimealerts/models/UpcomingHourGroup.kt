package com.justintimealerts.models

import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Represents a single hour slot in the upcoming-events calendar view.
 * All [events] share the same calendar date and clock hour.
 */
data class UpcomingHourGroup(
    /**
     * Human-readable label for the hour slot, e.g.
     * "Today  ·  2:00 PM", "Tomorrow  ·  9:00 AM", "Fri, Mar 13  ·  10:00 AM".
     */
    val hourLabel: String,
    /** All upcoming meetings that start within this hour slot. */
    val events: List<MeetingEvent>
) {
    /**
     * Formatted text ready for display.
     * A single event is shown as "Title  9:00 – 9:30 AM".
     * Multiple events are bullet-pointed, one per line.
     */
    val summary: String
        get() {
            val timeFormatter = DateTimeFormatter.ofPattern("h:mm")
            val timeFormatterWithAmPm = DateTimeFormatter.ofPattern("h:mm a")
            val zone = ZoneId.systemDefault()
            
            return if (events.size == 1) {
                val e = events[0]
                val startLocal = e.start.atZone(zone)
                val endLocal = e.end.atZone(zone)
                "${e.title}  ${startLocal.format(timeFormatter)} – ${endLocal.format(timeFormatterWithAmPm)}"
            } else {
                events.joinToString("\n") { e ->
                    val startLocal = e.start.atZone(zone)
                    val endLocal = e.end.atZone(zone)
                    "• ${e.title}  ${startLocal.format(timeFormatter)} – ${endLocal.format(timeFormatterWithAmPm)}"
                }
            }
        }
}
