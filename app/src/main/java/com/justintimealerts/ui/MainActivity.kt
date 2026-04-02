package com.justintimealerts.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.justintimealerts.JitApplication
import com.justintimealerts.R
import com.justintimealerts.models.CalendarSource
import com.justintimealerts.models.UpcomingHourGroup
import com.justintimealerts.services.CalendarSyncWorker
import com.justintimealerts.services.MeetingAlertForegroundService
import com.justintimealerts.viewmodels.MainViewModel
import java.text.SimpleDateFormat
import java.util.*
import java.util.concurrent.TimeUnit

class MainActivity : AppCompatActivity() {
    
    companion object {
        private const val SYNC_WORK_NAME = "jit_calendar_sync"
    }
    
    private val viewModel: MainViewModel by viewModels()
    
    private lateinit var swipeRefresh: SwipeRefreshLayout
    private lateinit var etIcsUrl: EditText
    private lateinit var btnAddCalendar: Button
    private lateinit var tvStatus: TextView
    private lateinit var tvUpcomingEmpty: TextView
    private lateinit var rvUpcomingEvents: RecyclerView
    private lateinit var rvCalendarSources: RecyclerView
    private lateinit var btnForceSync: Button
    
    private val upcomingAdapter = UpcomingEventsAdapter()
    private val sourcesAdapter = CalendarSourcesAdapter { id -> viewModel.removeCalendar(id) }
    
    private val requestPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        if (isGranted) {
            (application as JitApplication).log.log("Notification permission granted.")
        } else {
            (application as JitApplication).log.log("Notification permission denied.")
        }
    }
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        
        setupViews()
        setupObservers()
        
        // Request notification permission on Android 13+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED) {
                requestPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
            }
        }
        
        // Start foreground service
        startForegroundAlertService()
        
        // Schedule WorkManager periodic task
        scheduleCalendarSync()
    }
    
    private fun setupViews() {
        swipeRefresh = findViewById(R.id.swipeRefresh)
        etIcsUrl = findViewById(R.id.etIcsUrl)
        btnAddCalendar = findViewById(R.id.btnAddCalendar)
        tvStatus = findViewById(R.id.tvStatus)
        tvUpcomingEmpty = findViewById(R.id.tvUpcomingEmpty)
        rvUpcomingEvents = findViewById(R.id.rvUpcomingEvents)
        rvCalendarSources = findViewById(R.id.rvCalendarSources)
        btnForceSync = findViewById(R.id.btnForceSync)
        
        // Setup RecyclerViews
        rvUpcomingEvents.layoutManager = LinearLayoutManager(this)
        rvUpcomingEvents.adapter = upcomingAdapter
        
        rvCalendarSources.layoutManager = LinearLayoutManager(this)
        rvCalendarSources.adapter = sourcesAdapter
        
        // Setup listeners
        swipeRefresh.setOnRefreshListener {
            viewModel.syncNow()
        }
        
        btnAddCalendar.setOnClickListener {
            viewModel.setIcsUrl(etIcsUrl.text.toString())
            viewModel.addCalendarUrl()
        }
        
        btnForceSync.setOnClickListener {
            viewModel.forceSync()
        }
    }
    
    private fun setupObservers() {
        viewModel.statusMessage.observe(this) { message ->
            tvStatus.text = message
            tvStatus.visibility = if (message.isNullOrBlank()) View.GONE else View.VISIBLE
        }
        
        viewModel.isRefreshing.observe(this) { isRefreshing ->
            swipeRefresh.isRefreshing = isRefreshing
        }
        
        viewModel.upcomingEvents.observe(this) { groups ->
            upcomingAdapter.submitList(groups)
            tvUpcomingEmpty.visibility = if (groups.isEmpty()) View.VISIBLE else View.GONE
            rvUpcomingEvents.visibility = if (groups.isEmpty()) View.GONE else View.VISIBLE
        }
        
        viewModel.calendarSources.observe(this) { sources ->
            sourcesAdapter.submitList(sources)
        }
        
        viewModel.icsUrl.observe(this) { url ->
            if (etIcsUrl.text.toString() != url) {
                etIcsUrl.setText(url)
            }
        }
    }
    
    private fun startForegroundAlertService() {
        val serviceIntent = Intent(this, MeetingAlertForegroundService::class.java).apply {
            action = MeetingAlertForegroundService.ACTION_START
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(serviceIntent)
        } else {
            startService(serviceIntent)
        }
    }
    
    private fun scheduleCalendarSync() {
        // Android OS minimum is 15 minutes; shorter values are silently clamped.
        val workRequest = PeriodicWorkRequestBuilder<CalendarSyncWorker>(
            15, TimeUnit.MINUTES
        ).build()
        
        WorkManager.getInstance(this)
            .enqueueUniquePeriodicWork(
                SYNC_WORK_NAME,
                ExistingPeriodicWorkPolicy.KEEP,
                workRequest
            )
    }
    
    // Adapter for Upcoming Events
    private class UpcomingEventsAdapter : RecyclerView.Adapter<UpcomingEventsAdapter.ViewHolder>() {
        private var items: List<UpcomingHourGroup> = emptyList()
        
        fun submitList(newItems: List<UpcomingHourGroup>) {
            items = newItems
            notifyDataSetChanged()
        }
        
        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val view = LayoutInflater.from(parent.context)
                .inflate(R.layout.item_upcoming_event, parent, false)
            return ViewHolder(view)
        }
        
        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val item = items[position]
            holder.tvHourLabel.text = item.hourLabel
            holder.tvSummary.text = item.summary
        }
        
        override fun getItemCount() = items.size
        
        class ViewHolder(view: View) : RecyclerView.ViewHolder(view) {
            val tvHourLabel: TextView = view.findViewById(R.id.tvHourLabel)
            val tvSummary: TextView = view.findViewById(R.id.tvSummary)
        }
    }
    
    // Adapter for Calendar Sources
    private class CalendarSourcesAdapter(
        private val onRemove: (String) -> Unit
    ) : RecyclerView.Adapter<CalendarSourcesAdapter.ViewHolder>() {
        private var items: List<CalendarSource> = emptyList()
        
        fun submitList(newItems: List<CalendarSource>) {
            items = newItems
            notifyDataSetChanged()
        }
        
        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
            val view = LayoutInflater.from(parent.context)
                .inflate(R.layout.item_calendar_source, parent, false)
            return ViewHolder(view)
        }
        
        override fun onBindViewHolder(holder: ViewHolder, position: Int) {
            val item = items[position]
            holder.tvDisplayName.text = item.displayName
            
            val dateFormat = SimpleDateFormat("MMM d, yyyy h:mm a", Locale.getDefault())
            holder.tvLastSync.text = if (item.lastSyncTime > 0) {
                "Last sync: ${dateFormat.format(Date(item.lastSyncTime))}"
            } else {
                "Never synced"
            }
            
            holder.btnRemove.setOnClickListener {
                onRemove(item.id)
            }
        }
        
        override fun getItemCount() = items.size
        
        class ViewHolder(view: View) : RecyclerView.ViewHolder(view) {
            val tvDisplayName: TextView = view.findViewById(R.id.tvDisplayName)
            val tvLastSync: TextView = view.findViewById(R.id.tvLastSync)
            val btnRemove: Button = view.findViewById(R.id.btnRemove)
        }
    }
}
