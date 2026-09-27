using ClientAgent.Shared.Models;

namespace ClientAgent.UI.Services;

public static class DeviceIcons
{
    public static IReadOnlyList<(GarageDeviceKind Kind, string Label, string Glyph)> All { get; } =
    [
        (GarageDeviceKind.Camera, "Camera", "\uE722"),
        (GarageDeviceKind.Dispenser, "Dispenser", "\uE8F2"),
        (GarageDeviceKind.Gate, "Gate", "\uE8B7"),
        (GarageDeviceKind.Fingerprint, "Fingerprint", "\uE928"),
        (GarageDeviceKind.CardReader, "Card Reader", "\uE8C7"),
        (GarageDeviceKind.Barrier, "Barrier", "\uE701"),
        (GarageDeviceKind.Sensor, "Sensor", "\uE957"),
        (GarageDeviceKind.Payment, "Payment", "\uE719"),
        (GarageDeviceKind.Other, "Other", "\uE968")
    ];

    public static string Label(GarageDeviceKind kind)
        => All.First(item => item.Kind == kind).Label;

    public static string Glyph(MonitorPointType type, GarageDeviceKind? kind, string? name = null)
    {
        if (type == MonitorPointType.Device)
        {
            if (kind is GarageDeviceKind device)
            {
                return All.First(item => item.Kind == device).Glyph;
            }

            if (ContainsAny(name, "camera", "كاميرا"))
            {
                return "\uE722";
            }

            if (ContainsAny(name, "fingerprint", "بصمة"))
            {
                return "\uE928";
            }

            if (ContainsAny(name, "dispenser", "دسبنسر", "ديسبنسر"))
            {
                return "\uE8F2";
            }

            if (ContainsAny(name, "gate", "بوابة"))
            {
                return "\uE8B7";
            }

            return "\uE968";
        }

        return type switch
        {
            MonitorPointType.Website => "\uE774",
            MonitorPointType.Application => "\uECAA",
            MonitorPointType.Database => "\uE8F1",
            MonitorPointType.Madkhal => "\uE704",
            _ => "\uE968"
        };
    }

    private static bool ContainsAny(string? text, params string[] parts)
        => !string.IsNullOrWhiteSpace(text)
           && parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));
}
