using System.Globalization;
using System.Windows.Data;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Monitoring;

namespace ClientAgent.UI.Converters;

/// <summary>
/// Display text for a monitor point field. ConverterParameter picks the format:
/// "Time" (UTC to local date and time), "Since" (how long ago), "Response" (milliseconds),
/// "Alert", "DeviceKind" or "Seconds".
/// </summary>
public sealed class MonitorPointFieldConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (parameter as string, value) switch
        {
            ("Time", DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ("Since", DateTime utc) => Ago(DateTime.UtcNow - utc),
            ("Response", double ms) => MonitorPointText.ResponseTime(ms),
            ("Alert", MonitorPointAlert alert) => MonitorPointText.Alert(alert),
            ("DeviceKind", GarageDeviceKind kind) => MonitorPointText.DeviceKind(kind),
            ("Seconds", int seconds) => seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s",
            (_, null) => "-",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string Ago(TimeSpan span)
    {
        if (span < TimeSpan.Zero || span.TotalSeconds < 60)
        {
            return $"{Math.Max(0, (int)span.TotalSeconds)} s";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} min";
        }

        return span.TotalDays < 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{(int)span.TotalDays}d {span.Hours}h";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
