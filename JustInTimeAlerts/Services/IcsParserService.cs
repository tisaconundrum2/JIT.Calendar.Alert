using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Services;

/// <summary>
/// Fetches and parses ICS calendars from a URL or a local file path,
/// returning strongly-typed <see cref="MeetingEvent"/> objects.
/// </summary>
/// <remarks>
/// Network-efficiency strategy (prevents tight-loop DOS behaviour):
/// <list type="bullet">
///   <item>URLs are re-fetched at most once every <see cref="MinFetchInterval"/> (15 min).</item>
///   <item>Every URL request sends <c>If-None-Match</c> / <c>If-Modified-Since</c> headers.
///         A <c>304 Not Modified</c> response returns the in-memory cache instantly.</item>
///   <item>Even on a 200 response the ICS is only re-parsed when the SHA-256
///         content hash differs from the previously cached value.</item>
///   <item>Consecutive fetch failures trigger exponential back-off (1 min → 5 min → 15 min)
///         so a broken feed never produces an infinite retry loop.</item>
/// </list>
/// </remarks>
public class IcsParserService
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /// <summary>Minimum time between actual HTTP fetches for the same URL.</summary>
    public static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    // -----------------------------------------------------------------------
    // Per-source in-memory cache
    // -----------------------------------------------------------------------

    private sealed class SourceCache
    {
        public string?                    ETag              { get; set; }
        public string?                    LastModified       { get; set; }
        public string?                    ContentHash        { get; set; }
        public IReadOnlyList<MeetingEvent> Events            { get; set; } = Array.Empty<MeetingEvent>();
        public DateTime                   LastFetchTime      { get; set; } = DateTime.MinValue;
        public int                        ConsecutiveFailures { get; set; }
        public DateTime                   BackoffUntil       { get; set; } = DateTime.MinValue;

        // For local files: track the last write time so we only re-parse on change.
        public DateTime                   FileLastWrite      { get; set; } = DateTime.MinValue;
    }

    private readonly Dictionary<string, SourceCache> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    // -----------------------------------------------------------------------

    private readonly HttpClient  _httpClient;
    private readonly DebugLogService _log;

    public IcsParserService(HttpClient httpClient, DebugLogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Wipes all in-memory cache entries (or a single entry keyed by
    /// <paramref name="cacheKey"/>) so that the next
    /// <see cref="GetEventsAsync"/> call performs an unconditional HTTP fetch
    /// and full re-parse, bypassing the minimum re-fetch interval, back-off,
    /// conditional-request headers, and content-hash dedup.
    /// </summary>
    /// <param name="cacheKey">
    /// The URL or file path that identifies the source to invalidate.
    /// Pass <c>null</c> (the default) to invalidate every source at once.
    /// </param>
    public void InvalidateCache(string? cacheKey = null)
    {
        lock (_cacheLock)
        {
            if (cacheKey is null)
            {
                foreach (var entry in _cache.Values)
                    ResetEntry(entry);

                _log.Log($"ICS cache: all {_cache.Count} source(s) invalidated for force-sync.");
            }
            else if (_cache.TryGetValue(cacheKey, out var entry))
            {
                ResetEntry(entry);
                _log.Log($"ICS cache: '{cacheKey}' invalidated for force-sync.");
            }
        }

        static void ResetEntry(SourceCache e)
        {
            e.LastFetchTime       = DateTime.MinValue;
            e.BackoffUntil        = DateTime.MinValue;
            e.ContentHash         = null;
            e.ETag                = null;
            e.LastModified        = null;
            e.FileLastWrite       = DateTime.MinValue;
            e.ConsecutiveFailures = 0;
        }
    }

    /// <summary>
    /// Loads events from the given <paramref name="source"/>.
    /// Returns cached events when no network fetch is needed.
    /// Returns an empty list on unrecoverable failure so callers stay clean.
    /// </summary>
    public async Task<IReadOnlyList<MeetingEvent>> GetEventsAsync(
        CalendarSource source,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(source.Url))
            return await FetchFromUrlAsync(source, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(source.FilePath) && File.Exists(source.FilePath))
            return await ReadFromFileAsync(source, cancellationToken).ConfigureAwait(false);

        _log.Log("ICS source has no URL or valid file path — skipping.");
        return Array.Empty<MeetingEvent>();
    }

    // -----------------------------------------------------------------------
    // URL path
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<MeetingEvent>> FetchFromUrlAsync(
        CalendarSource source,
        CancellationToken cancellationToken)
    {
        var cacheKey = source.Url!;
        SourceCache entry;
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(cacheKey, out entry!))
            {
                entry = new SourceCache();
                _cache[cacheKey] = entry;
            }
        }

        var now = DateTime.UtcNow;

        // ── Back-off guard ──────────────────────────────────────────────────
        if (now < entry.BackoffUntil)
        {
            _log.Log($"ICS [{source.DisplayName}]: in back-off until {entry.BackoffUntil:HH:mm:ss} UTC — returning cache.");
            return entry.Events;
        }

        // ── Minimum re-fetch interval ───────────────────────────────────────
        if (entry.Events.Count > 0 && (now - entry.LastFetchTime) < MinFetchInterval)
        {
            _log.Log($"ICS [{source.DisplayName}]: cache fresh (last fetch {(now - entry.LastFetchTime).TotalMinutes:F1} min ago) — skipping HTTP.");
            return entry.Events;
        }

        // ── Build conditional request ───────────────────────────────────────
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);

        if (!string.IsNullOrEmpty(entry.ETag))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(entry.ETag, isWeak: true));
        else if (!string.IsNullOrEmpty(entry.LastModified) &&
                 DateTimeOffset.TryParse(entry.LastModified, out var lmParsed))
            request.Headers.IfModifiedSince = lmParsed;

        HttpResponseMessage response;
        try
        {
            _log.Log($"ICS [{source.DisplayName}]: HTTP GET (ETag: {entry.ETag ?? "none"})");
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleFetchFailure(entry, source.DisplayName, ex);
        }

        using (response)
        {
            // ── 304 Not Modified ────────────────────────────────────────────
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                entry.ConsecutiveFailures = 0;
                entry.LastFetchTime = now;
                _log.Log($"ICS [{source.DisplayName}]: 304 Not Modified — using {entry.Events.Count} cached event(s).");
                return entry.Events;
            }

            // ── Non-success ─────────────────────────────────────────────────
            if (!response.IsSuccessStatusCode)
            {
                return HandleFetchFailure(entry, source.DisplayName,
                    new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"));
            }

            // ── Read body ───────────────────────────────────────────────────
            string icsContent;
            try
            {
                icsContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return HandleFetchFailure(entry, source.DisplayName, ex);
            }

            // ── Content-hash dedup (skip re-parse if identical) ─────────────
            var hash = ComputeHash(icsContent);
            if (hash == entry.ContentHash)
            {
                entry.ConsecutiveFailures = 0;
                entry.LastFetchTime = now;
                // Refresh cache-control metadata in case ETag changed.
                CaptureResponseHeaders(response, entry);
                _log.Log($"ICS [{source.DisplayName}]: 200 but content unchanged — using {entry.Events.Count} cached event(s).");
                return entry.Events;
            }

            // ── Parse new content ───────────────────────────────────────────
            _log.Log($"ICS [{source.DisplayName}]: new content ({icsContent.Length} chars) — parsing.");
            var parsed = ParseIcsContent(icsContent, source.Id);
            _log.Log($"ICS [{source.DisplayName}]: parsed {parsed.Count} event(s).");

            entry.ContentHash          = hash;
            entry.Events               = parsed;
            entry.LastFetchTime        = now;
            entry.ConsecutiveFailures  = 0;
            entry.BackoffUntil         = DateTime.MinValue;
            CaptureResponseHeaders(response, entry);

            return parsed;
        }
    }

    // -----------------------------------------------------------------------
    // File path
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<MeetingEvent>> ReadFromFileAsync(
        CalendarSource source,
        CancellationToken cancellationToken)
    {
        var cacheKey = source.FilePath!;
        SourceCache entry;
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(cacheKey, out entry!))
            {
                entry = new SourceCache();
                _cache[cacheKey] = entry;
            }
        }

        var lastWrite = File.GetLastWriteTimeUtc(source.FilePath!);
        if (entry.Events.Count > 0 && lastWrite == entry.FileLastWrite)
        {
            _log.Log($"ICS [{source.DisplayName}]: file unchanged — using {entry.Events.Count} cached event(s).");
            return entry.Events;
        }

        string icsContent;
        try
        {
            _log.Log($"ICS [{source.DisplayName}]: reading file.");
            icsContent = await File.ReadAllTextAsync(source.FilePath!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Log($"ERROR reading ICS file: {ex.GetType().Name}: {ex.Message}");
            return entry.Events; // return stale cache rather than empty
        }

        var parsed = ParseIcsContent(icsContent, source.Id);
        _log.Log($"ICS [{source.DisplayName}]: parsed {parsed.Count} event(s) from file.");

        entry.Events        = parsed;
        entry.FileLastWrite = lastWrite;
        entry.ContentHash   = ComputeHash(icsContent);

        return parsed;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IReadOnlyList<MeetingEvent> HandleFetchFailure(
        SourceCache entry, string displayName, Exception ex)
    {
        entry.ConsecutiveFailures++;
        var stepIdx    = Math.Min(entry.ConsecutiveFailures - 1, BackoffSteps.Length - 1);
        var backoff    = BackoffSteps[stepIdx];
        entry.BackoffUntil = DateTime.UtcNow + backoff;

        _log.Log($"ERROR fetching ICS [{displayName}] (failure #{entry.ConsecutiveFailures}): " +
                 $"{ex.GetType().Name}: {ex.Message}. " +
                 $"Back-off for {backoff.TotalMinutes:F0} min.");

        return entry.Events; // return stale cache rather than empty
    }

    private static void CaptureResponseHeaders(HttpResponseMessage response, SourceCache entry)
    {
        if (response.Headers.ETag != null)
            entry.ETag = response.Headers.ETag.Tag;

        if (response.Content.Headers.LastModified.HasValue)
            entry.LastModified = response.Content.Headers.LastModified.Value.ToString("R");
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Removes invalid combinations from RRULE lines where both COUNT and UNTIL are present.
    /// RFC 5545 §3.3.10 forbids supplying both; real-world feeds (Outlook, Google) do it anyway.
    /// We keep UNTIL and drop COUNT, since UNTIL is more precise.
    /// </summary>
    private static string SanitizeIcsContent(string icsContent)
    {
        // Step 1: unfold RFC 5545 line continuations (CRLF/LF followed by SPACE or TAB)
        // so that each logical property is on a single string we can inspect.
        var unfolded = System.Text.RegularExpressions.Regex.Replace(
            icsContent, @"\r?\n[ \t]", string.Empty);

        // Step 2: for every logical RRULE line that contains both UNTIL= and COUNT=,
        // strip COUNT= (keeping UNTIL= as it is more precise).
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            unfolded,
            @"(?im)^(RRULE[^:]*:[^\r\n]*UNTIL=[^\r\n]*)(?:;COUNT=\d+|COUNT=\d+;?)([^\r\n]*)$",
            "$1$2");

        // Also handle COUNT before UNTIL
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?im)^(RRULE[^:]*:(?:[^\r\n]*?))COUNT=\d+;([^\r\n]*UNTIL=[^\r\n]*)$",
            "$1$2");

        return sanitized;
    }

    /// <summary>Parses raw ICS text and returns a list of <see cref="MeetingEvent"/>.</summary>
    public IReadOnlyList<MeetingEvent> ParseIcsContent(string icsContent, Guid calendarSourceId)
    {
        if (string.IsNullOrWhiteSpace(icsContent))
            return Array.Empty<MeetingEvent>();

        try
        {
            icsContent = SanitizeIcsContent(icsContent);
            var calendar = Calendar.Load(icsContent);
            var events = new List<MeetingEvent>();

            foreach (CalendarEvent e in calendar.Events)
            {
                if (e.Start == null)
                    continue;

                var uid = string.IsNullOrWhiteSpace(e.Uid)
                    ? Guid.NewGuid().ToString()
                    : e.Uid;

                // For recurring events expand occurrences over the next 30 days.
                if (e.RecurrenceRules?.Count > 0)
                {
                    var rangeStart = new CalDateTime(DateTime.UtcNow);
                    var rangeEnd   = new CalDateTime(DateTime.UtcNow.AddDays(30));
                    var occurrences = e.GetOccurrences(rangeStart)
                        .TakeWhile(occ => occ.Period.StartTime <= rangeEnd);
                    foreach (var occ in occurrences)
                    {
                        events.Add(new MeetingEvent
                        {
                            Uid = $"{uid}_{occ.Period.StartTime.AsUtc:yyyyMMddHHmmss}",
                            Title = e.Summary ?? "(No Title)",
                            Start = occ.Period.StartTime.AsUtc,
                            End = occ.Period.EndTime?.AsUtc ?? occ.Period.StartTime.AsUtc.AddHours(1),
                            Description = e.Description,
                            Location = e.Location,
                            CalendarSourceId = calendarSourceId,
                        });
                    }
                }
                else
                {
                    events.Add(new MeetingEvent
                    {
                        Uid = uid,
                        Title = e.Summary ?? "(No Title)",
                        Start = e.Start.AsUtc,
                        End = e.End?.AsUtc ?? e.Start.AsUtc.AddHours(1),
                        Description = e.Description,
                        Location = e.Location,
                        CalendarSourceId = calendarSourceId,
                    });
                }
            }

            return events;
        }
        catch (Exception ex)
        {
            _log.Log($"ERROR parsing ICS content: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<MeetingEvent>();
        }
    }
}
