namespace JustInTimeAlerts.Models;

/// <summary>
/// Simplified representation of a calendar event derived from an ICS file.
/// </summary>
public class MeetingEvent
{
    /// <summary>Unique identifier from the ICS UID field.</summary>
    public string Uid { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    /// <summary>Source calendar that provided this event.</summary>
    public Guid CalendarSourceId { get; set; }
}
