using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Services;

/// <summary>
/// Manages the list of <see cref="CalendarSource"/> objects that are persisted
/// on the device using app preferences.
/// </summary>
public class CalendarSourceRepository
{
    private const string PrefKey = "calendar_sources_json";

    private readonly List<CalendarSource> _sources = new();

    public IReadOnlyList<CalendarSource> Sources => _sources.AsReadOnly();

    public CalendarSourceRepository()
    {
        Load();
    }

    public void Add(CalendarSource source)
    {
        _sources.Add(source);
        Save();
    }

    public void Remove(Guid id)
    {
        var found = _sources.FirstOrDefault(s => s.Id == id);
        if (found != null)
        {
            _sources.Remove(found);
            Save();
        }
    }

    public void UpdateLastSync(Guid id)
    {
        var found = _sources.FirstOrDefault(s => s.Id == id);
        if (found != null)
        {
            found.LastSyncTime = DateTime.UtcNow;
            Save();
        }
    }

    private void Save()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_sources);
        Preferences.Default.Set(PrefKey, json);
    }

    private void Load()
    {
        var json = Preferences.Default.Get(PrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<CalendarSource>>(json);
                if (loaded != null)
                    _sources.AddRange(loaded);
            }
            catch
            {
                // Corrupt data – start fresh.
                _sources.Clear();
            }
        }
    }
}
