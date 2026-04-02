using System.Text;

namespace JustInTimeAlerts.Services;

/// <summary>
/// A singleton in-memory log collector.  Any service can call <see cref="Log"/>
/// and the UI binds to <see cref="AllLogs"/> via <see cref="LogChanged"/>.
/// Crash exceptions are also flushed to <see cref="CrashLogPath"/> immediately
/// so they survive the process being killed.
/// </summary>
public class DebugLogService
{
    private readonly List<string> _entries = new();
    private readonly object _lock = new();

    /// <summary>Absolute path of the on-disk crash log file.</summary>
    public string CrashLogPath { get; } =
        Path.Combine(FileSystem.AppDataDirectory, "app_crash.log");

    /// <summary>Raised on the calling thread whenever a new entry is added or the log is cleared.</summary>
    public event Action? LogChanged;

    /// <summary>All log entries as a single string, newest entries last.</summary>
    public string AllLogs
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count == 0
                    ? "(no log entries yet)"
                    : string.Join(Environment.NewLine, _entries);
            }
        }
    }

    /// <summary>Appends a timestamped entry.</summary>
    public void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_lock)
        {
            _entries.Add(entry);
            // Keep the in-memory buffer from growing unbounded.
            if (_entries.Count > 500)
                _entries.RemoveAt(0);
        }

        LogChanged?.Invoke();
    }

    /// <summary>
    /// Logs an unhandled exception both in-memory and synchronously to
    /// <see cref="CrashLogPath"/> so the entry is persisted even if the
    /// process is about to terminate.
    /// </summary>
    public void LogException(string context, Exception ex)
    {
        var message = $"[CRASH] {context}: {ex}";
        Log(message);

        // Write to disk immediately — don't wait for a background flush.
        try
        {
            File.AppendAllText(
                CrashLogPath,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"
                + $"{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* best-effort — if the file write fails, in-memory log still has the entry */ }
    }

    /// <summary>
    /// Returns the contents of the on-disk crash log from a previous run,
    /// then clears the file so it does not accumulate indefinitely.
    /// Returns <c>null</c> when no file exists.
    /// </summary>
    public string? ConsumePreviousCrashLog()
    {
        try
        {
            if (!File.Exists(CrashLogPath))
                return null;

            var contents = File.ReadAllText(CrashLogPath);
            File.Delete(CrashLogPath);
            return string.IsNullOrWhiteSpace(contents) ? null : contents;
        }
        catch { return null; }
    }

    /// <summary>Removes all log entries.</summary>
    public void Clear()
    {
        lock (_lock)
            _entries.Clear();

        LogChanged?.Invoke();
    }
}
