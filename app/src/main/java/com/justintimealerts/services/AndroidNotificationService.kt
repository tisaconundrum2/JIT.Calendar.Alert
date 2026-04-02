package com.justintimealerts.services

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.os.Build
import androidx.core.app.NotificationCompat
import com.justintimealerts.R
import com.justintimealerts.models.MeetingEvent
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.concurrent.atomic.AtomicInteger

/**
 * Wraps the Android NotificationManager to post a status-bar
 * notification whenever a meeting is about to start.
 */
class AndroidNotificationService(private val context: Context) {
    
    companion object {
        const val CHANNEL_ID = "jit_meeting_alerts"
        const val CHANNEL_NAME = "Meeting Alerts"
        const val CHANNEL_DESCRIPTION = "Just-in-time notifications when a meeting starts."
    }
    
    private val notificationManager: NotificationManager =
        context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
    
    private val nextId = AtomicInteger(2000) // Start above foreground service ID
    private var alertPlayer: MediaPlayer? = null
    
    init {
        ensureChannelCreated()
    }
    
    private fun ensureChannelCreated() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val existingChannel = notificationManager.getNotificationChannel(CHANNEL_ID)
            if (existingChannel == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    CHANNEL_NAME,
                    NotificationManager.IMPORTANCE_HIGH
                ).apply {
                    description = CHANNEL_DESCRIPTION
                    enableVibration(true)
                }
                notificationManager.createNotificationChannel(channel)
            }
        }
    }
    
    /** Posts an Android notification for the supplied [meeting]. */
    fun notify(meeting: MeetingEvent) {
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setContentTitle("Meeting Starting: ${meeting.title}")
            .setContentText(buildContentText(meeting))
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()
        
        notificationManager.notify(nextId.getAndIncrement(), notification)
        
        playAlertSound()
    }
    
    /**
     * Plays alert sound from assets using MediaPlayer.
     * The player is held in a field so the GC cannot collect it mid-playback,
     * and is released automatically on completion.
     */
    private fun playAlertSound() {
        try {
            // Release any previously playing instance before starting a new one.
            alertPlayer?.release()
            alertPlayer = null
            
            val afd = context.assets.openFd("Time_Up.mp3")
            
            val player = MediaPlayer().apply {
                setDataSource(afd.fileDescriptor, afd.startOffset, afd.length)
                afd.close()
                
                setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_ALARM)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                        .build()
                )
                
                prepare()
                
                setOnCompletionListener { mp ->
                    mp.release()
                    alertPlayer = null
                }
            }
            
            alertPlayer = player // hold a strong reference so the GC can't collect mid-play
            player.start()
        } catch (_: Exception) {
            // Sound playback is best-effort; never let it break the notification.
        }
    }
    
    private fun buildContentText(meeting: MeetingEvent): String {
        val local = meeting.start.atZone(ZoneId.systemDefault())
        val timeFormatter = DateTimeFormatter.ofPattern("h:mm a")
        var text = "Started at ${local.format(timeFormatter)}"
        if (!meeting.location.isNullOrBlank()) {
            text += " · ${meeting.location}"
        }
        return text
    }
}
