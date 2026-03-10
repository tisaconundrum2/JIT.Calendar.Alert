namespace JustInTimeAlerts.Models;

/// <summary>
/// Represents a single hour slot in the upcoming-events calendar view.
/// All <see cref="Events"/> share the same calendar date and clock hour.
/// </summary>
public class UpcomingHourGroup
{
    /// <summary>
    /// Human-readable label for the hour slot, e.g.
    /// "Today  ·  2:00 PM", "Tomorrow  ·  9:00 AM", "Fri, Mar 13  ·  10:00 AM".
    /// </summary>
    public string HourLabel { get; set; } = string.Empty;

    /// <summary>All upcoming meetings that start within this hour slot.</summary>
    public List<MeetingEvent> Events { get; set; } = new();

    /// <summary>
    /// Formatted text ready for display.
    /// A single event is shown as "Title  9:00 – 9:30 AM".
    /// Multiple events are bullet-pointed, one per line.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Events.Count == 1)
            {
                var e = Events[0];
                return $"{e.Title}  {e.Start.ToLocalTime():h:mm} – {e.End.ToLocalTime():h:mm tt}";
            }

            return string.Join("\n", Events.Select(e =>
                $"• {e.Title}  {e.Start.ToLocalTime():h:mm} – {e.End.ToLocalTime():h:mm tt}"));
        }
    }
}
