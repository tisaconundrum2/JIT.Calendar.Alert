namespace JustInTimeAlerts.Models;

/// <summary>
/// Represents a calendar data source (ICS URL or local file path).
/// </summary>
public class CalendarSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Remote ICS URL (null when loading from file).</summary>
    public string? Url { get; set; }

    /// <summary>Local file path on the device (null when loading from URL).</summary>
    public string? FilePath { get; set; }

    /// <summary>Human-readable display name shown in the UI.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Url) ? Url : System.IO.Path.GetFileName(FilePath ?? "Unknown");

    public DateTime LastSyncTime { get; set; }

    public bool IsActive { get; set; } = true;
}
