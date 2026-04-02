using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTimeAlerts.Services;

namespace JustInTimeAlerts.ViewModels;

public partial class LogViewModel : ObservableObject, IDisposable
{
    private readonly DebugLogService _logger;

    [ObservableProperty]
    private string _debugLog = "(no log entries yet)";

    /// <summary>
    /// File path of the on-disk crash log, shown in the UI so the user
    /// knows where to find persisted crash details.
    /// </summary>
    public string CrashLogPath => _logger.CrashLogPath;

    public LogViewModel(DebugLogService logger)
    {
        _logger = logger;
        _logger.LogChanged += OnLogChanged;
        DebugLog = _logger.AllLogs;
    }

    private void OnLogChanged()
    {
        MainThread.BeginInvokeOnMainThread(() => DebugLog = _logger.AllLogs);
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        await Clipboard.Default.SetTextAsync(_logger.AllLogs);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logger.Clear();
    }

    public void Dispose()
    {
        _logger.LogChanged -= OnLogChanged;
    }
}
