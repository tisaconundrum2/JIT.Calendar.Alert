package com.justintimealerts

import android.app.Application
import com.justintimealerts.services.*
import okhttp3.OkHttpClient
import java.util.concurrent.TimeUnit

/**
 * Application class that initializes all services and acts as a simple DI container.
 */
class JitApplication : Application() {
    
    lateinit var log: DebugLogService
        private set
    
    lateinit var httpClient: OkHttpClient
        private set
    
    lateinit var icsParser: IcsParserService
        private set
    
    lateinit var calendarRepository: CalendarSourceRepository
        private set
    
    lateinit var processedCache: ProcessedMeetingCache
        private set
    
    lateinit var meetingAlertService: MeetingAlertService
        private set
    
    lateinit var notificationService: AndroidNotificationService
        private set
    
    override fun onCreate() {
        super.onCreate()
        
        // Initialize services
        log = DebugLogService(filesDir)
        
        // Replay any crash log written by the previous run
        val previousCrash = log.consumePreviousCrashLog()
        if (previousCrash != null) {
            log.log("[Previous session crash log]\n$previousCrash")
        }
        
        // Global exception handlers
        Thread.setDefaultUncaughtExceptionHandler { thread, ex ->
            log.logException("UncaughtException on thread ${thread.name}", ex)
        }
        
        httpClient = OkHttpClient.Builder()
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .addInterceptor { chain ->
                val request = chain.request().newBuilder()
                    .addHeader("User-Agent", "JIT-Calendar-Alert/1.0")
                    .build()
                chain.proceed(request)
            }
            .build()
        
        icsParser = IcsParserService(httpClient, log)
        calendarRepository = CalendarSourceRepository(this)
        processedCache = ProcessedMeetingCache()
        
        meetingAlertService = MeetingAlertService(
            icsParser,
            calendarRepository,
            processedCache,
            log
        )
        
        notificationService = AndroidNotificationService(this)
        
        log.log("JitApplication initialized successfully.")
    }
}
