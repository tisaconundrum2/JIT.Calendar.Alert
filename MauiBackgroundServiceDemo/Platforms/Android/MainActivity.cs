using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using MauiBackgroundServiceDemo.Platforms.Android;

namespace MauiBackgroundServiceDemo;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        StartAccelerometerForegroundService();
    }

    protected override void OnDestroy()
    {
        StopAccelerometerForegroundService();
        base.OnDestroy();
    }

    private void StartAccelerometerForegroundService()
    {
        var intent = new Intent(this, typeof(AccelerometerForegroundService));
        intent.SetAction(AccelerometerForegroundService.ActionStart);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            StartForegroundService(intent);
        else
            StartService(intent);
    }

    private void StopAccelerometerForegroundService()
    {
        var intent = new Intent(this, typeof(AccelerometerForegroundService));
        intent.SetAction(AccelerometerForegroundService.ActionStop);
        StartService(intent);
    }
}
