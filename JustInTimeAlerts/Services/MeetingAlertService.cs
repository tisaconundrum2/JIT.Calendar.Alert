using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Services;

/// <summary>
/// Core logic engine that decides which meetings need an alert right now.
/// This is platform-agnostic; Android-specific notification triggering is
/// handled by the platform service layer.
/// </summary>
public class MeetingAlertService
{
    private readonly IcsParserService _parser;
    private readonly CalendarSourceRepository _repository;
    private readonly ProcessedMeetingCache _cache;

    /// <summary>
    /// The "just-in-time" window: a meeting is considered starting "right now"
    /// if its start time falls within the last <see cref="AlertWindow"/>.
    /// </summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromMinutes(1);

    public event EventHandler<MeetingEvent>? MeetingStarting;

    public MeetingAlertService(
        IcsParserService parser,
        CalendarSourceRepository repository,
        ProcessedMeetingCache cache)
    {
        _parser = parser;
        _repository = repository;
        _cache = cache;
    }

    /// <summary>
    /// Checks all active calendar sources for meetings that are starting right
    /// now and raises <see cref="MeetingStarting"/> for each unalerted meeting.
    /// </summary>
    public async Task CheckAndAlertAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var windowStart = now - AlertWindow;

        // Evict stale UIDs from the cache on each check cycle.
        _cache.Evict();

        var activeSources = _repository.Sources.Where(s => s.IsActive).ToList();

        foreach (var source in activeSources)
        {
            var events = await _parser.GetEventsAsync(source, cancellationToken);
            _repository.UpdateLastSync(source.Id);

            foreach (var meeting in events)
            {
                // Meeting is "starting right now" if its start time is within the alert window.
                if (meeting.Start >= windowStart && meeting.Start <= now)
                {
                    if (_cache.ShouldAlert(meeting.Uid))
                    {
                        _cache.MarkAlerted(meeting.Uid, meeting.End);
                        MeetingStarting?.Invoke(this, meeting);
                    }
                }
            }
        }
    }
}
