package com.justintimealerts.services

import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.CopyOnWriteArrayList

/**
 * A singleton in-memory log collector. Any service can call [log]
 * and the UI observes via [logChangedListeners].
 * Crash exceptions are also flushed to [crashLogPath] immediately
 * so they survive the process being killed.
 */
class DebugLogService(private val appDataDir: File) {
    
    private val entries = CopyOnWriteArrayList<String>()
    private val maxEntries = 500
    
    /** Absolute path of the on-disk crash log file. */
    val crashLogPath: File = File(appDataDir, "app_crash.log")
    
    /** Listeners notified when log content changes. */
    val logChangedListeners = mutableListOf<() -> Unit>()
    
    /** All log entries as a single string, newest entries last. */
    val allLogs: String
        get() = if (entries.isEmpty()) {
            "(no log entries yet)"
        } else {
            entries.joinToString("\n")
        }
    
    /** Appends a timestamped entry. */
    @Synchronized
    fun log(message: String) {
        val timestamp = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date())
        val entry = "[$timestamp] $message"
        entries.add(entry)
        
        // Keep the in-memory buffer from growing unbounded.
        while (entries.size > maxEntries) {
            entries.removeAt(0)
        }
        
        notifyListeners()
    }
    
    /**
     * Logs an unhandled exception both in-memory and synchronously to
     * [crashLogPath] so the entry is persisted even if the process is about to terminate.
     */
    fun logException(context: String, ex: Throwable) {
        val message = "[CRASH] $context: ${ex.stackTraceToString()}"
        log(message)
        
        // Write to disk immediately — don't wait for a background flush.
        try {
            val timestamp = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault()).format(Date())
            crashLogPath.appendText("=== $timestamp ===\n$message\n\n")
        } catch (_: Exception) {
            // best-effort — if the file write fails, in-memory log still has the entry
        }
    }
    
    /**
     * Returns the contents of the on-disk crash log from a previous run,
     * then clears the file so it does not accumulate indefinitely.
     * Returns null when no file exists.
     */
    fun consumePreviousCrashLog(): String? {
        return try {
            if (!crashLogPath.exists()) {
                null
            } else {
                val contents = crashLogPath.readText()
                crashLogPath.delete()
                if (contents.isBlank()) null else contents
            }
        } catch (_: Exception) {
            null
        }
    }
    
    /** Removes all log entries. */
    @Synchronized
    fun clear() {
        entries.clear()
        notifyListeners()
    }
    
    private fun notifyListeners() {
        logChangedListeners.forEach { it() }
    }
}
