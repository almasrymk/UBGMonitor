using System.Globalization;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

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
