#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using JustInTimeAlerts.Models;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// Wraps the Android <see cref="NotificationManager"/> to post a status-bar
/// notification whenever a meeting is about to start.
/// </summary>
public class AndroidNotificationService
{
    public const string ChannelId = "jit_meeting_alerts";
    public const string ChannelName = "Meeting Alerts";
    public const string ChannelDescription = "Just-in-time notifications when a meeting starts.";

    private readonly NotificationManager? _notificationManager;
    private int _nextId = 1;

    public AndroidNotificationService()
    {
        _notificationManager = (NotificationManager?)
            global::Android.App.Application.Context.GetSystemService(Context.NotificationService);

        EnsureChannelCreated();
    }

    private void EnsureChannelCreated()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        if (_notificationManager?.GetNotificationChannel(ChannelId) == null)
        {
            var channel = new NotificationChannel(
                ChannelId,
                ChannelName,
                NotificationImportance.High)
            {
                Description = ChannelDescription,
            };
            channel.EnableVibration(true);
            _notificationManager?.CreateNotificationChannel(channel);
        }
    }

    /// <summary>Posts an Android notification for the supplied <paramref name="meeting"/>.</summary>
    public void Notify(MeetingEvent meeting)
    {
        var context = global::Android.App.Application.Context;

        var builder = new Notification.Builder(context, ChannelId)
            .SetContentTitle($"Meeting Starting: {meeting.Title}")
            .SetContentText(BuildContentText(meeting))
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true);

        _notificationManager?.Notify(_nextId++, builder.Build());
    }

    private static string BuildContentText(MeetingEvent meeting)
    {
        var local = meeting.Start.ToLocalTime();
        var text = $"Started at {local:h:mm tt}";
        if (!string.IsNullOrWhiteSpace(meeting.Location))
            text += $" · {meeting.Location}";
        return text;
    }
}
#endif
