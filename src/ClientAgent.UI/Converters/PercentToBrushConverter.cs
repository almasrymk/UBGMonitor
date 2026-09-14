using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClientAgent.UI.Converters;

public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = ToDouble(value);
        if (percent >= 90) return Brush("AccentRed");
        if (percent >= 75) return Brush("AccentYellow");
        return Brush("AccentGreen");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => 0d
    };

    private static Brush Brush(string key)
        => (Brush)Application.Current.FindResource(key + "Brush");
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return parameter?.ToString() ?? string.Empty;
        }

        return Binding.DoNothing;
    }
}

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Application.Current.FindResource("AccentGreenBrush")
            : Application.Current.FindResource("AccentRedBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        if (text.Contains("Critical", StringComparison.OrdinalIgnoreCase))
        {
            return Application.Current.FindResource("AccentRedBrush");
        }

        if (text.Contains("Warning", StringComparison.OrdinalIgnoreCase))
        {
            return Application.Current.FindResource("AccentYellowBrush");
        }

        if (text.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return Application.Current.FindResource("AccentGrayBrush");
        }

        return Application.Current.FindResource("AccentGreenBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double gb)
        {
            return "-";
        }

        return gb >= 1024
            ? $"{gb / 1024:0.0} TB"
            : $"{gb:0} GB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class DiskStatusHelper
{
    public static string FromUsage(double percent)
        => percent >= 90 ? "Critical" : percent >= 75 ? "Warning" : "Healthy";
}

public sealed class HealthPercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 100d
        };

        if (percent >= 80) return Application.Current.FindResource("AccentGreenBrush");
        if (percent >= 50) return Application.Current.FindResource("AccentYellowBrush");
        return Application.Current.FindResource("AccentRedBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PauseLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Resume" : "Pause";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
