#if ANDROID
using Android.Content;
using AndroidX.Work;
using JustInTimeAlerts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// WorkManager <see cref="Worker"/> that runs <see cref="MeetingAlertService.CheckAndAlertAsync"/>
/// on a periodic schedule managed entirely by the Android OS — surviving app closure
/// and device reboots.
/// <para>
/// Android enforces a minimum interval of 15 minutes for
/// <see cref="PeriodicWorkRequest"/>; shorter intervals are silently clamped.
/// </para>
/// </summary>
public class CalendarSyncWorker : Worker
{
    public CalendarSyncWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams) { }

    public override Result DoWork()
    {
        try
        {
            // MainApplication (MauiApplication) is always instantiated before any
            // Android component runs, so the MAUI DI container is available here
            // even when the app UI is fully closed.
            var services = IPlatformApplication.Current?.Services;
            if (services == null)
                return Result.InvokeFailure();

            var alertService = services.GetService<MeetingAlertService>();
            var notificationService = services.GetService<AndroidNotificationService>();

            if (alertService == null || notificationService == null)
                return Result.InvokeFailure();

            // Wire notifications for this invocation.
            alertService.MeetingStarting += (_, meeting) => notificationService.Notify(meeting);

            // Worker is already on a background thread — block on the async work.
            alertService.CheckAndAlertAsync().GetAwaiter().GetResult();

            return Result.InvokeSuccess();
        }
        catch (Exception)
        {
            // Ask WorkManager to retry with its default back-off policy.
            return Result.InvokeRetry();
        }
    }
}
#endif
