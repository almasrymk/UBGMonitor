using CommunityToolkit.Mvvm.ComponentModel;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class DashboardMonitorPointViewModel : ObservableObject
{
    public string MonitorPointId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Glyph { get; init; } = "\uE968";

    public string Address { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public MonitorPointType Type { get; init; }

    public string Status { get; init; } = "Unknown";

    [ObservableProperty] private bool _isSelected;

    public static DashboardMonitorPointViewModel From(MonitorPointStatusDto point)
    {
        return new DashboardMonitorPointViewModel
        {
            MonitorPointId = point.MonitorPointId,
            DisplayName = string.IsNullOrWhiteSpace(point.DisplayName) ? point.MonitorPointId : point.DisplayName,
            Glyph = string.IsNullOrWhiteSpace(point.Glyph) ? DeviceIcons.Glyph(point.Type, point.DeviceKind, point.DisplayName) : point.Glyph,
            Address = point.Address,
            Icon = point.Icon,
            Type = point.Type,
            Status = string.IsNullOrWhiteSpace(point.Status) ? "Unknown" : point.Status
        };
    }
}
