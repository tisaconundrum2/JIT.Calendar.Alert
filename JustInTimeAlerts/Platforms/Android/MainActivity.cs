using Android.App;
using Android.Content.PM;
using Android.OS;

namespace JustInTimeAlerts;

[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
                               | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
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
    }
}
