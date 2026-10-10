using System.Globalization;
using Material.Icons;
using MonitorAgent.Shared.Models.Reports;

namespace MonitorAgent.Desktop.Converters;

public sealed class ReportTypeIconConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            ReportTypes.Summary => MaterialIconKind.ChartBoxOutline,
            ReportTypes.Details => MaterialIconKind.FileSearchOutline,
            ReportTypes.Incidents => MaterialIconKind.AlertOutline,
            ReportTypes.Availability => MaterialIconKind.CheckNetworkOutline,
            ReportTypes.Health => MaterialIconKind.HeartPulse,
            ReportTypes.Compliance => MaterialIconKind.ClipboardCheckOutline,
            ReportTypes.Internet => MaterialIconKind.Web,
            ReportTypes.Settings => MaterialIconKind.CogOutline,
            ReportTypes.Applications => MaterialIconKind.ViewGridOutline,
            ReportTypes.Inventory => MaterialIconKind.DesktopTowerMonitor,
            _ => MaterialIconKind.FileDocumentOutline
        };
}
