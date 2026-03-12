using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTimeAlerts.Models;
using JustInTimeAlerts.Services;

namespace JustInTimeAlerts.ViewModels;

/// <summary>
/// ViewModel for the main page.  Uses CommunityToolkit.Mvvm source generators
/// for commands and observable properties.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CalendarSourceRepository _repository;
    private readonly IcsParserService _parser;
    private readonly DebugLogService _logger;
    private readonly CancellationTokenSource _autoSyncCts = new();

    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromMinutes(5);

    [ObservableProperty]
    private string _icsUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _debugLog = "(no log entries yet)";

    [ObservableProperty]
    private bool _isRefreshing;

    public ObservableCollection<CalendarSource> CalendarSources { get; } = new();

    public ObservableCollection<UpcomingHourGroup> UpcomingEvents { get; } = new();

    public MainViewModel(CalendarSourceRepository repository, IcsParserService parser, DebugLogService debugLog)
    {
        _repository = repository;
        _parser = parser;
        _logger = debugLog;
        _logger.LogChanged += OnLogChanged;
        RefreshSources();
        _ = InitialLoadAsync();
        _ = StartAutoSyncAsync(_autoSyncCts.Token);
    }

    private async Task InitialLoadAsync()
    {
        var allEvents = await FetchAllEventsAsync();
        MainThread.BeginInvokeOnMainThread(() => UpdateUpcomingEvents(allEvents));
    }

    private async Task StartAutoSyncAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(AutoSyncInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                _logger.Log("Auto-sync triggered (5-minute interval).");
                await AutoSyncAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal.
        }
    }

    private async Task AutoSyncAsync(CancellationToken token)
    {
        var activeSources = _repository.Sources.Where(s => s.IsActive).ToList();
        var allEvents = await FetchAllEventsAsync(token);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshSources();
            UpdateUpcomingEvents(allEvents);
            StatusMessage = $"Auto-sync complete. {allEvents.Count} event(s) across {activeSources.Count} active calendar(s).";
        });
    }

    public void Dispose()
    {
        _autoSyncCts.Cancel();
        _autoSyncCts.Dispose();
        _logger.LogChanged -= OnLogChanged;
    }

    private void OnLogChanged()
    {
        MainThread.BeginInvokeOnMainThread(() => DebugLog = _logger.AllLogs);
    }

    private void RefreshSources()
    {
        CalendarSources.Clear();
        foreach (var source in _repository.Sources)
            CalendarSources.Add(source);
    }

    // -------------------------------------------------------------------------
    // Upcoming Events helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fetches all events from every active calendar source in one pass,
    /// updating last-sync timestamps. Returns the combined list.
    /// </summary>
    private async Task<List<MeetingEvent>> FetchAllEventsAsync(CancellationToken token = default)
    {
        var allEvents = new List<MeetingEvent>();
        foreach (var source in _repository.Sources.Where(s => s.IsActive))
        {
            var events = await _parser.GetEventsAsync(source, token);
            _repository.UpdateLastSync(source.Id);
            allEvents.AddRange(events);
        }
        return allEvents;
    }

    /// <summary>
    /// Filters <paramref name="allEvents"/> to future-only events, groups them
    /// by local calendar-hour slot, and replaces <see cref="UpcomingEvents"/>.
    /// Must be called on the UI thread.
    /// </summary>
    private void UpdateUpcomingEvents(IEnumerable<MeetingEvent> allEvents)
    {
        var nowUtc   = DateTime.UtcNow;
        var nowLocal = DateTime.Now;

        var todayLocal = nowLocal.Date;

        var groups = allEvents
            .Where(e => e.Start >= nowUtc && e.Start.ToLocalTime().Date == todayLocal)
            .OrderBy(e => e.Start)
            .GroupBy(e =>
            {
                var local = e.Start.ToLocalTime();
                return new { local.Date, local.Hour };
            })
            .Select(g =>
            {
                var slotLocal = g.Key.Date.AddHours(g.Key.Hour);
                return new UpcomingHourGroup
                {
                    HourLabel = FormatHourLabel(slotLocal, nowLocal),
                    Events    = g.ToList(),
                };
            })
            .ToList();

        UpcomingEvents.Clear();
        foreach (var group in groups)
            UpcomingEvents.Add(group);
    }

    private static string FormatHourLabel(DateTime slotLocal, DateTime nowLocal)
    {
        string dayPart;
        if (slotLocal.Date == nowLocal.Date)
            dayPart = "Today";
        else if (slotLocal.Date == nowLocal.Date.AddDays(1))
            dayPart = "Tomorrow";
        else
            dayPart = slotLocal.ToString("ddd, MMM d");

        return $"{dayPart}  \u00b7  {slotLocal:h:00 tt}";
    }

    [RelayCommand]
    private async Task AddCalendarUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(IcsUrl))
        {
            StatusMessage = "Please enter a valid ICS URL.";
            return;
        }

        _logger.Log($"Adding calendar URL: {IcsUrl.Trim()}");
        StatusMessage = "Validating URL…";
        var source = new CalendarSource { Url = IcsUrl.Trim() };

        // Validate by attempting a parse.
        var events = await _parser.GetEventsAsync(source);
        if (events.Count == 0)
        {
            _logger.Log("Validation failed: 0 events returned.");
            StatusMessage = "Could not load any events from that URL. Please check the address.";
            return;
        }

        _repository.Add(source);
        IcsUrl = string.Empty;
        RefreshSources();
        var allEvents = await FetchAllEventsAsync();
        UpdateUpcomingEvents(allEvents);
        StatusMessage = $"Calendar added ({events.Count} events found).";
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select an ICS file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "text/calendar", "application/ics", "*/*" } },
                }),
            });

            if (result == null)
                return;

            StatusMessage = "Reading file…";
            var source = new CalendarSource { FilePath = result.FullPath };
            var events = await _parser.GetEventsAsync(source);

            if (events.Count == 0)
            {
                StatusMessage = "No events found in the selected file.";
                return;
            }

            _repository.Add(source);
            RefreshSources();
            var allEventsAfterImport = await FetchAllEventsAsync();
            UpdateUpcomingEvents(allEventsAfterImport);
            StatusMessage = $"File imported ({events.Count} events found).";
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR importing file: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Error importing file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        _logger.Log("Manual sync triggered.");
        StatusMessage = "Syncing…";
        IsRefreshing = true;
        try
        {
            var activeSources = _repository.Sources.Where(s => s.IsActive).ToList();
            var allEvents = await FetchAllEventsAsync();
            RefreshSources();
            UpdateUpcomingEvents(allEvents);
            StatusMessage = $"Sync complete. {allEvents.Count} event(s) loaded across {activeSources.Count} active calendar(s).";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void RemoveCalendar(Guid id)
    {
        _repository.Remove(id);
        RefreshSources();
        StatusMessage = "Calendar removed.";
    }

    // -------------------------------------------------------------------------
    // Backup / Restore
    // -------------------------------------------------------------------------

    /// <summary>
    /// Exports every URL-based calendar source to a JSON file and opens the
    /// system share sheet so the user can save it to Downloads, Drive, etc.
    /// File-based sources are excluded because their paths are device-specific
    /// and will not survive a reinstall.
    /// </summary>
    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        try
        {
            var urlSources = _repository.Sources
                .Where(s => !string.IsNullOrWhiteSpace(s.Url))
                .Select(s => s.Url!)
                .ToList();

            if (urlSources.Count == 0)
            {
                StatusMessage = "Nothing to export – no URL-based calendars are saved.";
                return;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(urlSources,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var fileName = $"jit-calendar-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json";

#if ANDROID
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
            {
                // API 29+ – insert into the MediaStore Downloads collection.
                // No storage permission required.
                var values = new Android.Content.ContentValues();
                values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
                values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "application/json");
                values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath,
                    Android.OS.Environment.DirectoryDownloads);

                var resolver = Android.App.Application.Context.ContentResolver!;
                var uri = resolver.Insert(
                    Android.Provider.MediaStore.Downloads.ExternalContentUri!, values);

                if (uri == null)
                    throw new IOException("MediaStore returned a null URI – could not create file.");

                using var outStream = resolver.OpenOutputStream(uri)
                    ?? throw new IOException("Could not open output stream for MediaStore URI.");
                using var writer = new System.IO.StreamWriter(outStream);
                await writer.WriteAsync(json);
            }
            else
            {
                // API 26–28 – write directly to the public Downloads directory.
                // Requires WRITE_EXTERNAL_STORAGE permission (declared in AndroidManifest).
                var downloadsDir =
                    Android.OS.Environment.GetExternalStoragePublicDirectory(
                        Android.OS.Environment.DirectoryDownloads)!.AbsolutePath;
                var filePath = Path.Combine(downloadsDir, fileName);
                await File.WriteAllTextAsync(filePath, json);
            }
#endif

            _logger.Log($"Backup exported to Downloads: {fileName} ({urlSources.Count} URL(s)).");
            StatusMessage = $"Backup saved to Downloads/{fileName} ({urlSources.Count} calendar URL(s)).";
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR exporting backup: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Lets the user pick a previously exported backup JSON file and re-adds
    /// any URLs that are not already in the repository.
    /// </summary>
    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a JIT Calendar Backup (.json)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android,      new[] { "application/json", "*/*" } },
                    { DevicePlatform.iOS,           new[] { "public.json" } },
                    { DevicePlatform.MacCatalyst,   new[] { "public.json" } },
                    { DevicePlatform.WinUI,         new[] { ".json" } },
                }),
            });

            if (result == null)
                return;

            StatusMessage = "Reading backup file…";
            var json = await File.ReadAllTextAsync(result.FullPath);
            var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);

            if (urls == null || urls.Count == 0)
            {
                StatusMessage = "Backup file contained no calendar URLs.";
                return;
            }

            var existingUrls = _repository.Sources
                .Where(s => s.Url != null)
                .Select(s => s.Url!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0;
            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url) || existingUrls.Contains(url))
                    continue;

                _repository.Add(new CalendarSource { Url = url.Trim() });
                existingUrls.Add(url.Trim());
                added++;
            }

            RefreshSources();
            _logger.Log($"Backup imported: {added} new URL(s) added (skipped {urls.Count - added} duplicate(s)).");
            StatusMessage = added > 0
                ? $"Backup restored – {added} calendar(s) added."
                : "All URLs in the backup were already present; nothing added.";
        }
        catch (Exception ex)
        {
            _logger.Log($"ERROR importing backup: {ex.GetType().Name}: {ex.Message}");
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        await Clipboard.Default.SetTextAsync(_logger.AllLogs);
        StatusMessage = "Debug log copied to clipboard.";
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logger.Clear();
        StatusMessage = "Debug log cleared.";
    }

}
