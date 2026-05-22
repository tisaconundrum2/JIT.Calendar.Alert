#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using JustInTimeAlerts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// Android Foreground Service that keeps the app alive in the background.
/// A <see cref="PeriodicTimer"/> fires every 10 seconds to run the
/// <see cref="MeetingAlertService"/> logic engine.
/// </summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync,
         Exported = false)]
[Register("com.justintimealerts.MeetingAlertForegroundService")]
public class MeetingAlertForegroundService : Service
{
    public const int ForegroundNotificationId = 1001;
    public const string ActionStart = "com.justintimealerts.START";
    public const string ActionStop = "com.justintimealerts.STOP";
    
    private const int ShutdownTimeoutSeconds = 3;

    private CancellationTokenSource? _cts;
    private Task? _checkLoopTask;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            // Stop the service asynchronously to wait for pending tasks.
            // Fire-and-forget is safe here because Android keeps the service alive
            // until StopSelf() is called within StopServiceAsync().
            _ = StopServiceAsync();
            return StartCommandResult.NotSticky;
        }

        try
        {
            StartForeground(ForegroundNotificationId, BuildForegroundNotification());
        }
        catch (global::Android.App.ForegroundServiceStartNotAllowedException ex)
        {
            // Android 14+: the dataSync foreground service type has a 6-hour daily
            // time limit. When that limit is exhausted the OS throws here instead of
            // crashing the process — log it and stop gracefully. WorkManager's
            // CalendarSyncWorker will continue to run every 15 minutes as a fallback.
            var log = IPlatformApplication.Current?.Services?.GetService<DebugLogService>();
            log?.LogException("[ForegroundService] dataSync time limit exhausted; stopping gracefully", ex);
            
            // Ensure foreground state is stopped before stopping the service
            StopForegroundSafely();
            
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        _cts = new CancellationTokenSource();
        _checkLoopTask = RunCheckLoopAsync(_cts.Token);

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

            // Android 12+ (API 31) requires SCHEDULE_EXACT_ALARM or USE_EXACT_ALARM
            // to call SetExactAndAllowWhileIdle.  Check the runtime capability and
            // fall back to the inexact variant if permission was not granted.
            // The service is Sticky so Android will restart it regardless — the
            // alarm is only a belt-and-suspenders fallback.
            bool canExact = Build.VERSION.SdkInt < BuildVersionCodes.S
                            || alarmManager.CanScheduleExactAlarms();

            if (canExact && Build.VERSION.SdkInt >= BuildVersionCodes.M)
                alarmManager.SetExactAndAllowWhileIdle(
                    AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
            else
                alarmManager.SetAndAllowWhileIdle(
                    AlarmType.ElapsedRealtimeWakeup, triggerAt, pendingIntent);
        }

        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        // Use synchronous wait with timeout in OnDestroy to ensure cleanup
        // completes before the service is destroyed.
        if (_checkLoopTask != null && !_checkLoopTask.IsCompleted)
        {
            _cts?.Cancel();
            try
            {
                // Wait up to ShutdownTimeoutSeconds for the task to complete gracefully.
                // This is well within Android's foreground service stop timeout
                // and should be sufficient for CheckAndAlertAsync to complete.
                _checkLoopTask.Wait(TimeSpan.FromSeconds(ShutdownTimeoutSeconds));
            }
            catch (AggregateException)
            {
                // Expected if task was cancelled
            }
        }
        
        _cts?.Dispose();
        _cts = null;
        _checkLoopTask = null;
        
        // Ensure we're no longer in foreground state
        StopForegroundSafely();
        
        base.OnDestroy();
    }

    /// <summary>
    /// Asynchronously stops the service by canceling tasks and waiting for completion.
    /// </summary>
    private async Task StopServiceAsync()
    {
        // Cancel the token first to signal the loop to stop
        _cts?.Cancel();
        
        // Wait for the check loop to finish (with timeout)
        if (_checkLoopTask != null)
        {
            try
            {
                // Wait up to ShutdownTimeoutSeconds for graceful shutdown
                await _checkLoopTask.WaitAsync(TimeSpan.FromSeconds(ShutdownTimeoutSeconds)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Task didn't complete in time, but we'll proceed with shutdown anyway
            }
            catch (System.OperationCanceledException)
            {
                // Expected when the task was cancelled
            }
        }
        
        // Clean up
        _cts?.Dispose();
        _cts = null;
        _checkLoopTask = null;
        
        // Stop foreground state before stopping the service
        StopForegroundSafely();
        
        StopSelf();
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

    /// <summary>
    /// Stops the foreground state using the appropriate API for the current Android version.
    /// </summary>
    private void StopForegroundSafely()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CS0618 // Type or member is obsolete
            StopForeground(true);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
#endif
