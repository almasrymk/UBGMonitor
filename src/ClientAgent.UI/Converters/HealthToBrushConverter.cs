using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

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
