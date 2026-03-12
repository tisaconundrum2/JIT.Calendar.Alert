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
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync,
         StopWithTask = false)]          // keeps running when the user swipes the app away
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

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // Safety-net: even if Android somehow kills us after a swipe-away,
        // reschedule a restart ~1 second later via AlarmManager so alerts
        // never go dark for long.
        var restartIntent = new Intent(ApplicationContext, typeof(MeetingAlertForegroundService));
        restartIntent.SetAction(ActionStart);
        restartIntent.SetPackage(PackageName);

        var pendingIntent = PendingIntent.GetService(
            this, 1, restartIntent,
            PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var alarmManager = GetSystemService(AlarmService) as AlarmManager;
        if (alarmManager != null && pendingIntent != null)
        {
            long triggerAt = SystemClock.ElapsedRealtime() + 1_000; // ~1 second
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                alarmManager.SetExactAndAllowWhileIdle(
                    AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
            else
                alarmManager.Set(
                    AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
        }

        base.OnTaskRemoved(rootIntent);
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

        // Align to the next whole minute (XX:XX:00) so checks always fire at a
        // predictable time regardless of when the service was started.
        var now = DateTime.UtcNow;
        var msIntoMinute = now.Second * 1_000 + now.Millisecond;
        var alignDelay = msIntoMinute == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(60_000 - msIntoMinute);

        if (alignDelay > TimeSpan.Zero)
            await Task.Delay(alignDelay, token).ConfigureAwait(false);

        // Poll every 60 seconds — matches the 1-minute AlertWindow and avoids
        // hammering the network. ICS re-fetches are further rate-limited inside
        // IcsParserService (min 15-minute interval + ETag/If-Modified-Since).
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        // Run immediately at the minute boundary, then every 60 seconds.
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
