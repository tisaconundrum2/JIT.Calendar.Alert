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
public partial class MainViewModel : ObservableObject
{
    private readonly CalendarSourceRepository _repository;
    private readonly IcsParserService _parser;

    [ObservableProperty]
    private string _icsUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isServiceRunning;

    public ObservableCollection<CalendarSource> CalendarSources { get; } = new();

    public MainViewModel(CalendarSourceRepository repository, IcsParserService parser)
    {
        _repository = repository;
        _parser = parser;
        RefreshSources();
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

        StatusMessage = "Validating URL…";
        var source = new CalendarSource { Url = IcsUrl.Trim() };

        // Validate by attempting a parse.
        var events = await _parser.GetEventsAsync(source);
        if (events.Count == 0)
        {
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
            StatusMessage = $"Error importing file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
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
