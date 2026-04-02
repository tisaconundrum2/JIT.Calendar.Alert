package com.justintimealerts.services

import android.app.AlarmManager
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import androidx.core.app.NotificationCompat
import com.justintimealerts.JitApplication
import kotlinx.coroutines.*
import java.time.Duration
import java.time.Instant

/**
 * Android Foreground Service that keeps the app alive in the background.
 * A coroutine loop fires every 60 seconds to run the MeetingAlertService logic engine.
 */
class MeetingAlertForegroundService : Service() {
    
    companion object {
        const val FOREGROUND_NOTIFICATION_ID = 1001
        const val ACTION_START = "com.justintimealerts.START"
        const val ACTION_STOP = "com.justintimealerts.STOP"
        private const val CHANNEL_ID = "jit_foreground_service"
        private const val CHANNEL_NAME = "JIT Alerts Service"
    }
    
    private val serviceScope = CoroutineScope(Dispatchers.Default + SupervisorJob())
    private var checkJob: Job? = null
    
    override fun onBind(intent: Intent?): IBinder? = null
    
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        
        try {
            startForeground(FOREGROUND_NOTIFICATION_ID, buildForegroundNotification())
        } catch (e: Exception) {
            // Android 14+: the dataSync foreground service type has a 6-hour daily
            // time limit. When that limit is exhausted the OS throws here — log it and stop gracefully.
            val app = application as? JitApplication
            app?.log?.logException("[ForegroundService] dataSync time limit exhausted; stopping gracefully", e)
            stopSelf()
            return START_NOT_STICKY
        }
        
        startCheckLoop()
        
        return START_STICKY
    }
    
    override fun onTaskRemoved(rootIntent: Intent?) {
        // Safety-net: reschedule a restart ~1 second later via AlarmManager
        val restartIntent = Intent(applicationContext, MeetingAlertForegroundService::class.java).apply {
            action = ACTION_START
            setPackage(packageName)
        }
        
        val pendingIntent = PendingIntent.getService(
            this, 1, restartIntent,
            PendingIntent.FLAG_ONE_SHOT or PendingIntent.FLAG_IMMUTABLE
        )
        
        val alarmManager = getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val triggerAt = SystemClock.elapsedRealtime() + 1000 // ~1 second
        
        // Android 12+ requires SCHEDULE_EXACT_ALARM permission
        val canExact = Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
                alarmManager.canScheduleExactAlarms()
        
        if (canExact && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            alarmManager.setExactAndAllowWhileIdle(
                AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAt, pendingIntent
            )
        } else {
            alarmManager.setAndAllowWhileIdle(
                AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAt, pendingIntent
            )
        }
        
        super.onTaskRemoved(rootIntent)
    }
    
    override fun onDestroy() {
        checkJob?.cancel()
        serviceScope.cancel()
        super.onDestroy()
    }
    
    private fun startCheckLoop() {
        val app = application as? JitApplication ?: return
        val alertService = app.meetingAlertService
        val notificationService = app.notificationService
        
        // Register for meeting alerts
        alertService.meetingStartingListeners.add { meeting ->
            notificationService.notify(meeting)
        }
        
        checkJob = serviceScope.launch {
            // Align to the next whole minute (XX:XX:00) so checks always fire at a predictable time
            val now = Instant.now()
            val msIntoMinute = (now.epochSecond % 60) * 1000 + (now.nano / 1_000_000)
            val alignDelay = if (msIntoMinute == 0L) 0L else 60_000L - msIntoMinute
            
            if (alignDelay > 0) {
                delay(alignDelay)
            }
            
            // Run immediately at the minute boundary, then every 60 seconds
            alertService.checkAndAlert()
            
            while (isActive) {
                delay(60_000) // Poll every 60 seconds
                alertService.checkAndAlert()
            }
        }
    }
    
    private fun buildForegroundNotification(): Notification {
        // Ensure the channel exists
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val nm = getSystemService(NotificationManager::class.java)
            if (nm.getNotificationChannel(CHANNEL_ID) == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    CHANNEL_NAME,
                    NotificationManager.IMPORTANCE_LOW // Low importance for persistent service notification
                ).apply {
                    description = "Background service for monitoring calendar alerts"
                }
                nm.createNotificationChannel(channel)
            }
        }
        
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("JIT Alerts Running")
            .setContentText("Monitoring your calendar for upcoming meetings.")
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }
}
