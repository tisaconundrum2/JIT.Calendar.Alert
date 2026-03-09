using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Services;

/// <summary>
/// Thread-safe cache that tracks which meeting UIDs have already triggered an
/// alert.  Uses a <see cref="Dictionary{TKey,TValue}"/> mapping UIDs to their
/// meeting end times, enabling automatic eviction of entries after the meeting
/// has been over for more than <see cref="ExpiryAfterEnd"/>.
/// </summary>
public class ProcessedMeetingCache
{
    private readonly TimeSpan _expiryAfterEnd;

    /// <summary>Default time after a meeting ends before its UID is evicted from the cache.</summary>
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    private readonly Dictionary<string, DateTime> _alerted = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public TimeSpan ExpiryAfterEnd => _expiryAfterEnd;

    public ProcessedMeetingCache(TimeSpan? expiryAfterEnd = null)
    {
        _expiryAfterEnd = expiryAfterEnd ?? DefaultExpiry;
    }

    /// <summary>Returns <c>true</c> if the UID has not yet been recorded as alerted.</summary>
    public bool ShouldAlert(string uid) => !Contains(uid);

    /// <summary>Returns <c>true</c> if the cache contains the specified UID.</summary>
    public bool Contains(string uid)
    {
        lock (_lock)
        {
            return _alerted.ContainsKey(uid);
        }
    }

    /// <summary>
    /// Records a UID as alerted, storing the meeting's <paramref name="endTime"/>
    /// so the entry can be evicted once the meeting has been over long enough.
    /// </summary>
    public void MarkAlerted(string uid, DateTime endTime)
    {
        lock (_lock)
        {
            _alerted[uid] = endTime;
        }
    }

    /// <summary>Removes all entries whose meetings ended more than <see cref="ExpiryAfterEnd"/> ago.</summary>
    public void Evict()
    {
        var cutoff = DateTime.UtcNow - _expiryAfterEnd;
        lock (_lock)
        {
            var expired = _alerted
                .Where(kv => kv.Value < cutoff)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expired)
                _alerted.Remove(key);
        }
    }

    /// <summary>Returns the number of UIDs currently in the cache (for diagnostics).</summary>
    public int Count
    {
        get
        {
            lock (_lock) { return _alerted.Count; }
        }
    }
}
