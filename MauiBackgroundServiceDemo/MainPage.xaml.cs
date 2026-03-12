using Microsoft.Maui.Devices.Sensors;
using MauiBackgroundServiceDemo.Services;

namespace MauiBackgroundServiceDemo;

public partial class MainPage : ContentPage
{
    private int _readingCount;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AccelerometerBackgroundService.ReadingChanged += OnReadingChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        AccelerometerBackgroundService.ReadingChanged -= OnReadingChanged;
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        var d = e.Reading.Acceleration;
        _readingCount++;
        int count = _readingCount;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            XLabel.Text = $"{d.X:+0.000;-0.000} g";
            YLabel.Text = $"{d.Y:+0.000;-0.000} g";
            ZLabel.Text = $"{d.Z:+0.000;-0.000} g";
            ReadingCountLabel.Text = $"Readings received: {count:N0}";

            StatusDot.Fill = new SolidColorBrush(Colors.LimeGreen);
            StatusLabel.Text = "Service running";
        });
    }
}
