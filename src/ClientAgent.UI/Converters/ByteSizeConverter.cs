using System.Globalization;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

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
