using System.Globalization;
using System.Windows.Data;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.UI.Converters;

public sealed class MonitorPointTypeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MonitorPointType type ? MonitorPointTypeLabels.Format(type) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
