using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace MauiBackgroundServiceDemo.Platforms.Android;

/// <summary>
/// A native Android foreground service that displays a persistent notification,
/// preventing the OS from aggressively killing the process while the accelerometer
/// background service is running.
/// </summary>
[Service(
    Name = "com.companyname.mauibackgroundservicedemo.AccelerometerForegroundService",
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public class AccelerometerForegroundService : Service
{
    private const int NotificationId = 1001;
    public const string ChannelId = "accel_bg_service_channel";
    public const string ActionStart = "ACTION_START_ACCEL_SERVICE";
    public const string ActionStop = "ACTION_STOP_ACCEL_SERVICE";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CreateNotificationChannel();

        // Tapping the notification will bring the app to the foreground
        var launchIntent = global::Android.App.Application.Context
            .PackageManager?
            .GetLaunchIntentForPackage(global::Android.App.Application.Context.PackageName ?? string.Empty);

        var pendingIntent = PendingIntent.GetActivity(
            this, 0, launchIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Accelerometer Monitor")
            .SetContentText("Monitoring accelerometer data in the background…")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCompass)
            .SetOngoing(true)
            .SetContentIntent(pendingIntent)
            .Build();

        StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "Accelerometer Background Service",
            NotificationImportance.Low)
        {
            Description = "Keeps the accelerometer monitor running in the background."
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}
