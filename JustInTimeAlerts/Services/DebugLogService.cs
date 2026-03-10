using System.Text;

namespace JustInTimeAlerts.Services;

/// <summary>
/// A singleton in-memory log collector.  Any service can call <see cref="Log"/>
/// and the UI binds to <see cref="AllLogs"/> via <see cref="LogChanged"/>.
/// </summary>
public class DebugLogService
{
    private readonly List<string> _entries = new();
    private readonly object _lock = new();

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

    /// <summary>Removes all log entries.</summary>
    public void Clear()
    {
        lock (_lock)
            _entries.Clear();

        LogChanged?.Invoke();
    }
}
