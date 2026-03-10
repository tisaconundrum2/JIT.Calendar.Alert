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
    private bool _isServiceRunning;

    [ObservableProperty]
    private string _debugLog = "(no log entries yet)";

    public ObservableCollection<CalendarSource> CalendarSources { get; } = new();

    public MainViewModel(CalendarSourceRepository repository, IcsParserService parser, DebugLogService debugLog)
    {
        _repository = repository;
        _parser = parser;
        _logger = debugLog;
        _logger.LogChanged += OnLogChanged;
        RefreshSources();
        _ = StartAutoSyncAsync(_autoSyncCts.Token);
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
        var total = 0;
        var activeSources = _repository.Sources.Where(s => s.IsActive).ToList();

        foreach (var source in activeSources)
        {
            var events = await _parser.GetEventsAsync(source, token);
            total += events.Count;
            _repository.UpdateLastSync(source.Id);
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshSources();
            StatusMessage = $"Auto-sync complete. {total} event(s) across {activeSources.Count} active calendar(s).";
        });
    }

    public void Dispose()
    {
        _autoSyncCts.Cancel();
        _autoSyncCts.Dispose();
    }

    private void OnLogChanged()
    {
        // Must update the bound property on the UI thread.
        MainThread.BeginInvokeOnMainThread(() => DebugLog = _logger.AllLogs);
    }

    private void RefreshSources()
    {
        CalendarSources.Clear();
        foreach (var source in _repository.Sources)
            CalendarSources.Add(source);
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
        var total = 0;
        var activeSources = _repository.Sources.Where(s => s.IsActive).ToList();

        foreach (var source in activeSources)
        {
            var events = await _parser.GetEventsAsync(source);
            total += events.Count;
            _repository.UpdateLastSync(source.Id);
        }

        RefreshSources();
        StatusMessage = $"Sync complete. {total} event(s) loaded across {activeSources.Count} active calendar(s).";
    }

    [RelayCommand]
    private void RemoveCalendar(Guid id)
    {
        _repository.Remove(id);
        RefreshSources();
        StatusMessage = "Calendar removed.";
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

    [RelayCommand]
    private void ToggleService()
    {
#if ANDROID
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(
            context,
            typeof(Platforms.Android.Services.MeetingAlertForegroundService));

        if (IsServiceRunning)
        {
            intent.SetAction(Platforms.Android.Services.MeetingAlertForegroundService.ActionStop);
            context.StartService(intent);
            IsServiceRunning = false;
            StatusMessage = "Background monitoring stopped.";
        }
        else
        {
            intent.SetAction(Platforms.Android.Services.MeetingAlertForegroundService.ActionStart);
            context.StartForegroundService(intent);
            IsServiceRunning = true;
            StatusMessage = "Background monitoring started.";
        }
#else
        StatusMessage = "Background service is only supported on Android.";
#endif
    }
}
