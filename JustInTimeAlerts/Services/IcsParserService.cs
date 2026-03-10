using Ical.Net;
using Ical.Net.CalendarComponents;
using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Services;

/// <summary>
/// Fetches and parses ICS calendars from a URL or a local file path,
/// returning strongly-typed <see cref="MeetingEvent"/> objects.
/// </summary>
public class IcsParserService
{
    private readonly HttpClient _httpClient;
    private readonly DebugLogService _log;

    public IcsParserService(HttpClient httpClient, DebugLogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    /// <summary>
    /// Loads events from the given <paramref name="source"/>.
    /// Returns an empty list on failure so callers can handle errors gracefully.
    /// </summary>
    public async Task<IReadOnlyList<MeetingEvent>> GetEventsAsync(
        CalendarSource source,
        CancellationToken cancellationToken = default)
    {
        string icsContent;
        try
        {
            if (!string.IsNullOrWhiteSpace(source.Url))
            {
                _log.Log($"Fetching ICS from URL: {source.Url}");
                icsContent = await _httpClient.GetStringAsync(source.Url, cancellationToken);
                _log.Log($"Fetched {icsContent.Length} chars from URL.");
            }
            else if (!string.IsNullOrWhiteSpace(source.FilePath) && File.Exists(source.FilePath))
            {
                _log.Log($"Reading ICS file: {source.FilePath}");
                icsContent = await File.ReadAllTextAsync(source.FilePath, cancellationToken);
                _log.Log($"Read {icsContent.Length} chars from file.");
            }
            else
            {
                _log.Log("ICS source has no URL or valid file path — skipping.");
                return Array.Empty<MeetingEvent>();
            }
        }
        catch (Exception ex)
        {
            _log.Log($"ERROR fetching ICS: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<MeetingEvent>();
        }

        var result = ParseIcsContent(icsContent, source.Id);
        _log.Log($"Parsed {result.Count} event(s) from source.");
        return result;
    }

    /// <summary>Parses raw ICS text and returns a list of <see cref="MeetingEvent"/>.</summary>
    public IReadOnlyList<MeetingEvent> ParseIcsContent(string icsContent, Guid calendarSourceId)
    {
        if (string.IsNullOrWhiteSpace(icsContent))
            return Array.Empty<MeetingEvent>();

        try
        {
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
                    var occurrences = e.GetOccurrences(DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
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
