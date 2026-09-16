using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DashboardMonitorPointViewModel : ObservableObject
{
    public string MonitorPointId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Glyph { get; init; } = "\uE968";

    public string Status { get; init; } = "Unknown";

    [ObservableProperty] private bool _isSelected;

    public static DashboardMonitorPointViewModel From(MonitorPointStatusDto point)
    {
        return new DashboardMonitorPointViewModel
        {
            MonitorPointId = point.MonitorPointId,
            DisplayName = string.IsNullOrWhiteSpace(point.DisplayName) ? point.MonitorPointId : point.DisplayName,
            Glyph = ResolveGlyph(point),
            Status = string.IsNullOrWhiteSpace(point.Status) ? "Unknown" : point.Status
        };
    }

    private static string ResolveGlyph(MonitorPointStatusDto point)
    {
        var haystack = $"{point.DisplayName} {point.Model} {point.Type}";
        if (ContainsAny(haystack, "camera", "كاميرا"))
        {
            return "\uE722";
        }

        if (ContainsAny(haystack, "satel", "dish", "قمر"))
        {
            return "\uEC05";
        }

        if (point.Type == MonitorPointType.Madkhal || ContainsAny(haystack, "wifi", "radio"))
        {
            return "\uE704";
        }

        if (point.Type == MonitorPointType.Database)
        {
            return "\uE8F1";
        }

        return "\uE968";
    }

    private static bool ContainsAny(string text, params string[] parts)
        => parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));
}
