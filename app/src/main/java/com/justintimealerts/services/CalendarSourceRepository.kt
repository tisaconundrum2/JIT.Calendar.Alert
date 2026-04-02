package com.justintimealerts.services

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import com.justintimealerts.models.CalendarSource

/**
 * Manages the list of [CalendarSource] objects that are persisted
 * on the device using SharedPreferences.
 */
class CalendarSourceRepository(context: Context) {
    
    companion object {
        private const val PREF_NAME = "calendar_sources_prefs"
        private const val PREF_KEY = "calendar_sources_json"
    }
    
    private val prefs: SharedPreferences = context.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE)
    private val gson = Gson()
    private val _sources = mutableListOf<CalendarSource>()
    
    val sources: List<CalendarSource>
        get() = _sources.toList()
    
    init {
        load()
    }
    
    fun add(source: CalendarSource) {
        _sources.add(source)
        save()
    }
    
    fun remove(id: String) {
        val found = _sources.find { it.id == id }
        if (found != null) {
            _sources.remove(found)
            save()
        }
    }
    
    fun updateLastSync(id: String) {
        val found = _sources.find { it.id == id }
        if (found != null) {
            found.lastSyncTime = System.currentTimeMillis()
            save()
        }
    }
    
    private fun save() {
        val json = gson.toJson(_sources)
        prefs.edit().putString(PREF_KEY, json).apply()
    }
    
    private fun load() {
        val json = prefs.getString(PREF_KEY, null)
        if (!json.isNullOrBlank()) {
            try {
                val type = object : TypeToken<List<CalendarSource>>() {}.type
                val loaded: List<CalendarSource>? = gson.fromJson(json, type)
                if (loaded != null) {
                    _sources.addAll(loaded)
                }
            } catch (_: Exception) {
                // Corrupt data – start fresh.
                _sources.clear()
            }
        }
    }
}
