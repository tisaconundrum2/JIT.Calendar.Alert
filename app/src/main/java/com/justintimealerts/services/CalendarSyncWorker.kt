package com.justintimealerts.services

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.justintimealerts.JitApplication

/**
 * WorkManager Worker that runs MeetingAlertService.checkAndAlert()
 * on a periodic schedule managed entirely by the Android OS — surviving app closure
 * and device reboots.
 *
 * Android enforces a minimum interval of 15 minutes for PeriodicWorkRequest;
 * shorter intervals are silently clamped.
 */
class CalendarSyncWorker(
    context: Context,
    workerParams: WorkerParameters
) : CoroutineWorker(context, workerParams) {
    
    override suspend fun doWork(): Result {
        return try {
            val app = applicationContext as? JitApplication
                ?: return Result.failure()
            
            val alertService = app.meetingAlertService
            val notificationService = app.notificationService
            
            // Register listener for this work unit
            val listener: (com.justintimealerts.models.MeetingEvent) -> Unit = { meeting ->
                notificationService.notify(meeting)
            }
            
            alertService.meetingStartingListeners.add(listener)
            try {
                alertService.checkAndAlert()
            } finally {
                alertService.meetingStartingListeners.remove(listener)
            }
            
            Result.success()
        } catch (_: Exception) {
            // Ask WorkManager to retry with its default back-off policy.
            Result.retry()
        }
    }
}
