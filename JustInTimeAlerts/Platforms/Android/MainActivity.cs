using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Work;
using JustInTimeAlerts.Platforms.Android.Services;

namespace JustInTimeAlerts;

[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
                               | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>Unique name used to avoid duplicate WorkManager entries.</summary>
    private const string SyncWorkName = "jit_calendar_sync";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Request POST_NOTIFICATIONS permission on Android 13+ (API 33+).
        if (Build.VERSION.SdkInt >= (BuildVersionCodes)33)
        {
            RequestPermissions(
                new[] { "android.permission.POST_NOTIFICATIONS" },
                requestCode: 1001);
        }

        ScheduleCalendarSync();
    }

    /// <summary>
    /// Enqueues a unique periodic WorkManager task that syncs calendars and
    /// raises meeting alerts every 15 minutes — even when the app is closed.
    /// <para>
    /// <see cref="ExistingPeriodicWorkPolicy.Keep"/> means a second call (e.g.
    /// on subsequent app launches) is a no-op, so the schedule is never reset.
    /// </para>
    /// </summary>
    private static void ScheduleCalendarSync()
    {
        // Android OS minimum is 15 minutes; shorter values are silently clamped.
        var workRequest = new PeriodicWorkRequest.Builder(
                Java.Lang.Class.FromType(typeof(CalendarSyncWorker)),
                15,
                Java.Util.Concurrent.TimeUnit.Minutes!)
            .Build();

        WorkManager
            .GetInstance(global::Android.App.Application.Context)
            .EnqueueUniquePeriodicWork(
                SyncWorkName,
                ExistingPeriodicWorkPolicy.Keep,
                workRequest);
    }
}
