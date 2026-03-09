using Microsoft.Extensions.Logging;
using JustInTimeAlerts.Services;
using JustInTimeAlerts.ViewModels;

namespace JustInTimeAlerts;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Infrastructure
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<IcsParserService>();
        builder.Services.AddSingleton<CalendarSourceRepository>();
        builder.Services.AddSingleton<ProcessedMeetingCache>();
        builder.Services.AddSingleton<MeetingAlertService>();

#if ANDROID
        builder.Services.AddSingleton<Platforms.Android.Services.AndroidNotificationService>();
#endif

        // UI
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
