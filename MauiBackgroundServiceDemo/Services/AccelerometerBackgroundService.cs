using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices.Sensors;

namespace MauiBackgroundServiceDemo.Services;

/// <summary>
/// A .NET hosted background service that monitors accelerometer readings.
/// Exposes the latest reading via a static event so any UI component can subscribe.
/// </summary>
public class AccelerometerBackgroundService : BackgroundService
{
    private readonly ILogger<AccelerometerBackgroundService> _logger;

    /// <summary>
    /// Raised on every new accelerometer reading so the UI can update without
    /// a direct dependency on this service.
    /// </summary>
    public static event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;

    public AccelerometerBackgroundService(ILogger<AccelerometerBackgroundService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AccelerometerBackgroundService started.");

        // Wire up the sensor on the main thread (MAUI requirement for sensors)
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Accelerometer.Default.ReadingChanged += OnReadingChanged;
            if (!Accelerometer.Default.IsMonitoring)
                Accelerometer.Default.Start(SensorSpeed.UI);
        });

        // Keep alive until the app shuts down or the service is stopped
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        // Clean up on the main thread
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Accelerometer.Default.IsMonitoring)
                Accelerometer.Default.Stop();
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        });

        _logger.LogInformation("AccelerometerBackgroundService stopped.");
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        var d = e.Reading.Acceleration;
        _logger.LogDebug("Accel X={X:0.000}  Y={Y:0.000}  Z={Z:0.000}", d.X, d.Y, d.Z);

        // Propagate to any subscriber (e.g. MainPage)
        ReadingChanged?.Invoke(this, e);
    }
}
