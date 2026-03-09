#if ANDROID
using Android.App;
using Android.Content;

namespace JustInTimeAlerts.Platforms.Android.Services;

/// <summary>
/// Starts the <see cref="MeetingAlertForegroundService"/> automatically when
/// the device reboots.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted || context == null)
            return;

        var serviceIntent = new Intent(context, typeof(MeetingAlertForegroundService));
        serviceIntent.SetAction(MeetingAlertForegroundService.ActionStart);
        context.StartForegroundService(serviceIntent);
    }
}
#endif
