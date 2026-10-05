using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;
using Material.Icons;

namespace MonitorAgent.Desktop.Converters;

internal static class StatusBrushes
{
    public static IBrush Green => UiTheme.Brush("AccentGreenBrush", Color.FromRgb(0x4C, 0xAF, 0x50));
    public static IBrush Yellow => UiTheme.Brush("AccentYellowBrush", Color.FromRgb(0xFF, 0xC1, 0x07));
    public static IBrush Red => UiTheme.Brush("AccentRedBrush", Color.FromRgb(0xF4, 0x43, 0x36));
    public static IBrush Gray => UiTheme.Brush("AccentGrayBrush", Color.FromRgb(0x9E, 0x9E, 0x9E));

    public static double ToDouble(object? value, double fallback = 0) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => fallback
    };
}

public abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

public sealed class BoolToStatusBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? StatusBrushes.Green : StatusBrushes.Red;
}

public sealed class ByteSizeConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not double gb ? "-" : gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
}

/// <summary>True when the value's text equals the parameter; checking a radio button sets the value to the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() ?? string.Empty : BindingOperations.DoNothing;
}

public sealed class NotEqualsConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
}

public sealed class HealthPercentToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = StatusBrushes.ToDouble(value, 100);
        return percent >= 80 ? StatusBrushes.Green : percent >= 50 ? StatusBrushes.Yellow : StatusBrushes.Red;
    }
}

public sealed class HealthToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Contains("Critical", StringComparison.OrdinalIgnoreCase))
        {
            return StatusBrushes.Red;
        }

        if (text.Contains("Warning", StringComparison.OrdinalIgnoreCase))
        {
            return StatusBrushes.Yellow;
        }

        return text.Contains("Unknown", StringComparison.OrdinalIgnoreCase) ? StatusBrushes.Gray : StatusBrushes.Green;
    }
}

public sealed class IconToImageConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string icon ? SiteLogoCache.ToImage(icon) : null;
}

/// <summary>
/// Display text for a monitor point field. ConverterParameter picks the format:
/// "Time" (UTC to local date and time), "Since" (how long ago), "Response" (milliseconds),
/// "Alert", "DeviceKind" or "Seconds".
/// </summary>
public sealed class MonitorPointFieldConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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
}

public sealed class MonitorPointTypeLabelConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MonitorPointType type ? MonitorPointTypeLabels.Format(type) : value?.ToString() ?? string.Empty;
}

public sealed class PercentToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = StatusBrushes.ToDouble(value);
        return percent >= 90 ? StatusBrushes.Red : percent >= 75 ? StatusBrushes.Yellow : StatusBrushes.Green;
    }
}

public sealed class ProcessBarBrushConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = values.Count > 0 ? StatusBrushes.ToDouble(values[0]) : 0d;
        var sortBy = values.Count > 1 && values[1] is ProcessSortBy parsed ? parsed : ProcessSortBy.Cpu;
        return sortBy switch
        {
            ProcessSortBy.Ram => UiTheme.Brush("ProcessBarRamBrush", Color.FromRgb(0x21, 0x96, 0xF3)),
            ProcessSortBy.Network => UiTheme.Brush("TextPrimaryBrush", Colors.White),
            ProcessSortBy.Disk => UiTheme.Brush("ProcessBarDiskBrush", Color.FromRgb(0xAB, 0x47, 0xBC)),
            _ when percent >= 50 => UiTheme.Brush("ProcessBarCpuHighBrush", Color.FromRgb(0xF4, 0x43, 0x36)),
            _ when percent >= 20 => UiTheme.Brush("ProcessBarCpuMidBrush", Color.FromRgb(0xFF, 0xC1, 0x07)),
            _ => UiTheme.Brush("ProcessBarCpuLowBrush", Color.FromRgb(0x4C, 0xAF, 0x50))
        };
    }
}

public sealed class SensorHealthToBrushConverter : OneWayConverter
{
    private static readonly IBrush Ok = new ImmutableSolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush Warning = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly IBrush Critical = new ImmutableSolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SensorHealth.Ok ? Ok : value is SensorHealth.Warning ? Warning : Critical;
}

/// <summary>Colour of an issue title by its severity (Warning, Unknown, Success; anything else is a problem).</summary>
public sealed class SeverityToBrushConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() switch
    {
        "Warning" => StatusBrushes.Yellow,
        "Unknown" => StatusBrushes.Gray,
        "Success" => StatusBrushes.Green,
        _ => StatusBrushes.Red
    };
}

/// <summary>Picks one of two brush resources: the parameter is "TrueKey|FalseKey".</summary>
public sealed class BoolToResourceBrushConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var keys = (parameter as string ?? string.Empty).Split('|');
        var key = value is true ? keys[0] : keys.ElementAtOrDefault(1) ?? keys[0];
        return key.StartsWith('#') ? new SolidColorBrush(Color.Parse(key)) : UiTheme.Brush(key, Colors.Gray);
    }
}

/// <summary>One of two texts by a bool: the parameter is "TrueText|FalseText".</summary>
public sealed class BoolToTextConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texts = (parameter as string ?? string.Empty).Split('|');
        return value is true ? texts[0] : texts.ElementAtOrDefault(1) ?? string.Empty;
    }
}

/// <summary>The Material icon drawn for a Segoe MDL2 glyph used by the shared view models.</summary>
public sealed class GlyphToIconConverter : OneWayConverter
{
    private static readonly Dictionary<char, MaterialIconKind> Icons = new()
    {
        ['\uE700'] = MaterialIconKind.Menu,
        ['\uE701'] = MaterialIconKind.Wifi,
        ['\uE704'] = MaterialIconKind.AccessPointNetwork,
        ['\uE707'] = MaterialIconKind.MapMarker,
        ['\uE70D'] = MaterialIconKind.ChevronDown,
        ['\uE711'] = MaterialIconKind.Close,
        ['\uE71D'] = MaterialIconKind.Apps,
        ['\uE713'] = MaterialIconKind.Cog,
        ['\uE719'] = MaterialIconKind.CashRegister,
        ['\uE722'] = MaterialIconKind.Cctv,
        ['\uE72C'] = MaterialIconKind.Refresh,
        ['\uE73E'] = MaterialIconKind.Check,
        ['\uE76C'] = MaterialIconKind.ChevronRight,
        ['\uE774'] = MaterialIconKind.Web,
        ['\uE77B'] = MaterialIconKind.Account,
        ['\uE7BA'] = MaterialIconKind.Alert,
        ['\uE80F'] = MaterialIconKind.Home,
        ['\uE839'] = MaterialIconKind.Ethernet,
        ['\uE8B7'] = MaterialIconKind.Gate,
        ['\uE8BB'] = MaterialIconKind.Close,
        ['\uE8C7'] = MaterialIconKind.CreditCard,
        ['\uE8F1'] = MaterialIconKind.Database,
        ['\uE8F2'] = MaterialIconKind.TicketConfirmation,
        ['\uE928'] = MaterialIconKind.Fingerprint,
        ['\uE946'] = MaterialIconKind.Information,
        ['\uE957'] = MaterialIconKind.MotionSensor,
        ['\uE968'] = MaterialIconKind.Devices,
        ['\uE9D2'] = MaterialIconKind.ChartAreaspline,
        ['\uECAA'] = MaterialIconKind.Application
    };

    public static MaterialIconKind Icon(string? glyph)
        => glyph is { Length: > 0 } && Icons.TryGetValue(glyph[0], out var kind) ? kind : MaterialIconKind.Devices;

    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Icon(value as string);
}
