package com.justintimealerts.models

import java.util.UUID

/**
 * Represents a calendar data source (ICS URL or local file path).
 */
data class CalendarSource(
    val id: String = UUID.randomUUID().toString(),
    val url: String? = null,
    val filePath: String? = null,
    var lastSyncTime: Long = 0L,
    var isActive: Boolean = true
) {
    /**
     * Human-readable display name shown in the UI.
     */
    val displayName: String
        get() = when {
            !url.isNullOrBlank() -> url
            !filePath.isNullOrBlank() -> filePath.substringAfterLast("/")
            else -> "Unknown"
        }
}
