using System.Globalization;

namespace JustInTimeAlerts.Converters;

/// <summary>
/// Converts the <c>IsServiceRunning</c> boolean to a button label string.
/// </summary>
public class ServiceButtonTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Stop Background Monitoring" : "Start Background Monitoring";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
