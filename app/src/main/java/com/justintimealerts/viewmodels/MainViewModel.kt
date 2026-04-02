package com.justintimealerts.viewmodels

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import androidx.lifecycle.viewModelScope
import com.justintimealerts.JitApplication
import com.justintimealerts.models.CalendarSource
import com.justintimealerts.models.MeetingEvent
import com.justintimealerts.models.UpcomingHourGroup
import kotlinx.coroutines.*
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * ViewModel for the main page.
 */
class MainViewModel(application: Application) : AndroidViewModel(application) {
    
    companion object {
        private val AUTO_SYNC_INTERVAL_MS = 5 * 60 * 1000L // 5 minutes
    }
    
    private val app: JitApplication = application as JitApplication
    private val repository = app.calendarRepository
    private val parser = app.icsParser
    private val log = app.log
    
    private var autoSyncJob: Job? = null
    
    // LiveData for UI
    private val _icsUrl = MutableLiveData("")
    val icsUrl: LiveData<String> = _icsUrl
    
    private val _statusMessage = MutableLiveData("")
    val statusMessage: LiveData<String> = _statusMessage
    
    private val _debugLog = MutableLiveData("(no log entries yet)")
    val debugLog: LiveData<String> = _debugLog
    
    private val _isRefreshing = MutableLiveData(false)
    val isRefreshing: LiveData<Boolean> = _isRefreshing
    
    private val _calendarSources = MutableLiveData<List<CalendarSource>>(emptyList())
    val calendarSources: LiveData<List<CalendarSource>> = _calendarSources
    
    private val _upcomingEvents = MutableLiveData<List<UpcomingHourGroup>>(emptyList())
    val upcomingEvents: LiveData<List<UpcomingHourGroup>> = _upcomingEvents
    
    init {
        log.logChangedListeners.add { onLogChanged() }
        refreshSources()
        
        viewModelScope.launch {
            initialLoad()
            startAutoSync()
        }
    }
    
    fun setIcsUrl(url: String) {
        _icsUrl.value = url
    }
    
    private suspend fun initialLoad() {
        val allEvents = fetchAllEvents()
        withContext(Dispatchers.Main) {
            updateUpcomingEvents(allEvents)
        }
    }
    
    private suspend fun startAutoSync() {
        autoSyncJob = viewModelScope.launch {
            while (isActive) {
                delay(AUTO_SYNC_INTERVAL_MS)
                log.log("Auto-sync triggered (5-minute interval).")
                autoSync()
            }
        }
    }
    
    private suspend fun autoSync() {
        val allEvents = fetchAllEvents()
        val activeSources = repository.sources.filter { it.isActive }
        
        withContext(Dispatchers.Main) {
            refreshSources()
            updateUpcomingEvents(allEvents)
            _statusMessage.value = "Auto-sync complete. ${allEvents.size} event(s) across ${activeSources.size} active calendar(s)."
        }
    }
    
    private fun onLogChanged() {
        viewModelScope.launch(Dispatchers.Main) {
            _debugLog.value = log.allLogs
        }
    }
    
    private fun refreshSources() {
        _calendarSources.value = repository.sources.toList()
    }
    
    private suspend fun fetchAllEvents(): List<MeetingEvent> = withContext(Dispatchers.IO) {
        val allEvents = mutableListOf<MeetingEvent>()
        for (source in repository.sources.filter { it.isActive }) {
            val events = parser.getEvents(source)
            repository.updateLastSync(source.id)
            allEvents.addAll(events)
        }
        allEvents
    }
    
    private fun updateUpcomingEvents(allEvents: List<MeetingEvent>) {
        val nowUtc = Instant.now()
        val zone = ZoneId.systemDefault()
        val todayLocal = LocalDate.now(zone)
        val nowLocal = nowUtc.atZone(zone).toLocalDateTime()
        
        val hourFormatter = DateTimeFormatter.ofPattern("h:00 a")
        
        val groups = allEvents
            .filter { it.start >= nowUtc && it.start.atZone(zone).toLocalDate() == todayLocal }
            .sortedBy { it.start }
            .groupBy { event ->
                val local = event.start.atZone(zone).toLocalDateTime()
                local.toLocalDate() to local.hour
            }
            .map { (key, events) ->
                val (date, hour) = key
                val slotLocal = date.atTime(hour, 0)
                UpcomingHourGroup(
                    hourLabel = formatHourLabel(slotLocal, nowLocal, hourFormatter),
                    events = events
                )
            }
        
        _upcomingEvents.value = groups
    }
    
    private fun formatHourLabel(
        slotLocal: java.time.LocalDateTime,
        nowLocal: java.time.LocalDateTime,
        hourFormatter: DateTimeFormatter
    ): String {
        val dayPart = when {
            slotLocal.toLocalDate() == nowLocal.toLocalDate() -> "Today"
            slotLocal.toLocalDate() == nowLocal.toLocalDate().plusDays(1) -> "Tomorrow"
            else -> slotLocal.format(DateTimeFormatter.ofPattern("EEE, MMM d"))
        }
        return "$dayPart  ·  ${slotLocal.format(hourFormatter)}"
    }
    
    fun addCalendarUrl() {
        val url = _icsUrl.value?.trim()
        if (url.isNullOrBlank()) {
            _statusMessage.value = "Please enter a valid ICS URL."
            return
        }
        
        log.log("Adding calendar URL: $url")
        _statusMessage.value = "Validating URL…"
        
        viewModelScope.launch {
            val source = CalendarSource(url = url)
            
            // Validate by attempting a parse
            val events = parser.getEvents(source)
            if (events.isEmpty()) {
                log.log("Validation failed: 0 events returned.")
                withContext(Dispatchers.Main) {
                    _statusMessage.value = "Could not load any events from that URL. Please check the address."
                }
                return@launch
            }
            
            repository.add(source)
            withContext(Dispatchers.Main) {
                _icsUrl.value = ""
                refreshSources()
            }
            
            val allEvents = fetchAllEvents()
            withContext(Dispatchers.Main) {
                updateUpcomingEvents(allEvents)
                _statusMessage.value = "Calendar added (${events.size} events found)."
            }
        }
    }
    
    fun syncNow() {
        log.log("Manual sync triggered.")
        _statusMessage.value = "Syncing…"
        _isRefreshing.value = true
        
        viewModelScope.launch {
            try {
                val activeSources = repository.sources.filter { it.isActive }
                val allEvents = fetchAllEvents()
                
                withContext(Dispatchers.Main) {
                    refreshSources()
                    updateUpcomingEvents(allEvents)
                    _statusMessage.value = "Sync complete. ${allEvents.size} event(s) loaded across ${activeSources.size} active calendar(s)."
                }
            } finally {
                withContext(Dispatchers.Main) {
                    _isRefreshing.value = false
                }
            }
        }
    }
    
    /**
     * Bypasses every layer of caching and forces a full re-download and re-parse.
     */
    fun forceSync() {
        log.log("Force sync triggered – invalidating all caches.")
        _statusMessage.value = "Force syncing…"
        _isRefreshing.value = true
        
        viewModelScope.launch {
            try {
                parser.invalidateCache()
                
                val activeSources = repository.sources.filter { it.isActive }
                val allEvents = fetchAllEvents()
                
                withContext(Dispatchers.Main) {
                    refreshSources()
                    updateUpcomingEvents(allEvents)
                    _statusMessage.value = "Force sync complete. ${allEvents.size} event(s) loaded across ${activeSources.size} active calendar(s)."
                }
            } finally {
                withContext(Dispatchers.Main) {
                    _isRefreshing.value = false
                }
            }
        }
    }
    
    fun removeCalendar(id: String) {
        repository.remove(id)
        refreshSources()
        _statusMessage.value = "Calendar removed."
    }
    
    fun copyLogs() {
        val clipboard = getApplication<JitApplication>().getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
        val clip = android.content.ClipData.newPlainText("Debug Log", log.allLogs)
        clipboard.setPrimaryClip(clip)
        _statusMessage.value = "Debug log copied to clipboard."
    }
    
    fun clearLogs() {
        log.clear()
        _statusMessage.value = "Debug log cleared."
    }
    
    override fun onCleared() {
        super.onCleared()
        autoSyncJob?.cancel()
        log.logChangedListeners.clear()
    }
}
