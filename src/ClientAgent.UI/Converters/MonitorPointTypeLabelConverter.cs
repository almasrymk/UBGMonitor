using System.Globalization;
using System.Windows.Data;
using ClientAgent.Shared.Models;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Converters;

public sealed class MonitorPointTypeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MonitorPointType type ? MonitorPointTypeLabels.Format(type) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
