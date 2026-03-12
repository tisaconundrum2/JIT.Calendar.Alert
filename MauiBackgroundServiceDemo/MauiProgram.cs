using Microsoft.Extensions.Logging;
using MauiBackgroundServiceDemo.Services;

namespace MauiBackgroundServiceDemo;

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

		// Register the accelerometer background service (runs for the app lifetime)
		builder.Services.AddHostedService<AccelerometerBackgroundService>();

		// Register MainPage so it can be resolved via DI if needed
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
