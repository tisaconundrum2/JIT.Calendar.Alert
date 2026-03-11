using System.Net;
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
        builder.Services.AddSingleton(_ =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            var client = new HttpClient(handler)
            {
                // Hard cap per request — prevents a hung server from pinning the
                // background thread indefinitely and blocking the next sync cycle.
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JIT-Calendar-Alert/1.0");
            return client;
        });
        builder.Services.AddSingleton<DebugLogService>();
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
        builder.Services.AddTransient<JustInTimeAlerts.ViewModels.LogViewModel>();
        builder.Services.AddTransient<JustInTimeAlerts.Views.LogPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
