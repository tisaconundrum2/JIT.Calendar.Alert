#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using JustInTimeAlerts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// Android Foreground Service that keeps the app alive in the background.
/// A <see cref="PeriodicTimer"/> fires every 10 seconds to run the
/// <see cref="MeetingAlertService"/> logic engine.
/// </summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public class MeetingAlertForegroundService : Service
{
    public const int ForegroundNotificationId = 1001;
    public const string ActionStart = "com.justintimealerts.START";
    public const string ActionStop = "com.justintimealerts.STOP";

    private CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        StartForeground(ForegroundNotificationId, BuildForegroundNotification());
        _cts = new CancellationTokenSource();
        _ = RunCheckLoopAsync(_cts.Token);

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnDestroy();
    }

    private async Task RunCheckLoopAsync(CancellationToken token)
    {
        var services = IPlatformApplication.Current?.Services;
        if (services == null)
            return;

        var alertService = services.GetService<MeetingAlertService>();
        var notificationService = services.GetService<AndroidNotificationService>();

        if (alertService == null || notificationService == null)
            return;

        alertService.MeetingStarting += (_, meeting) => notificationService.Notify(meeting);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        // Run immediately on first tick, then every 10 seconds.
        await alertService.CheckAndAlertAsync(token).ConfigureAwait(false);

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await alertService.CheckAndAlertAsync(token).ConfigureAwait(false);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected when service is stopping.
        }
    }

    private Notification BuildForegroundNotification()
    {
        const string channelId = AndroidNotificationService.ChannelId;

        // Ensure the channel exists.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            if (nm?.GetNotificationChannel(channelId) == null)
            {
                var channel = new NotificationChannel(
                    channelId,
                    AndroidNotificationService.ChannelName,
                    NotificationImportance.Low) // Low importance for persistent service notification
                {
                    Description = AndroidNotificationService.ChannelDescription,
                };
                nm?.CreateNotificationChannel(channel);
            }
        }

        var builder = new Notification.Builder(this, channelId)
            .SetContentTitle("JIT Alerts Running")
            .SetContentText("Monitoring your calendar for upcoming meetings.")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true);

        return builder.Build();
    }
}
#endif
