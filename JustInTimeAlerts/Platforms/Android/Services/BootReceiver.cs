#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Work;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// Re-schedules the periodic <see cref="CalendarSyncWorker"/> via WorkManager
/// when the device reboots.
/// <para>
/// Android 14+ (API 34) forbids starting a <c>dataSync</c> foreground service
/// directly from a BOOT_COMPLETED broadcast receiver
/// (<c>ForegroundServiceStartNotAllowedException</c>). WorkManager is explicitly
/// designed to survive reboots and does not carry this restriction, so it is used
/// here instead of calling <c>StartForegroundService</c>. The foreground service
/// continues to be started from <c>MainActivity.OnCreate</c> when the user opens
/// the app.
/// </para>
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted || context == null)
            return;

        // Re-enqueue the periodic WorkManager task. ExistingPeriodicWorkPolicy.Keep
        // is a no-op if the work is already queued, so this is safe to call
        // unconditionally on every boot.
        var workRequest = new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(CalendarSyncWorker)),
                CalendarSyncWorker.RepeatIntervalMinutes,
                Java.Util.Concurrent.TimeUnit.Minutes!)
            .Build();

        WorkManager
            .GetInstance(context)
            .EnqueueUniquePeriodicWork(
                CalendarSyncWorker.UniqueName,
                ExistingPeriodicWorkPolicy.Keep,
                workRequest);
    }
}
#endif
