using System.Globalization;
using System.Text.Json;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Mac;

/// <summary>
/// The battery (pmset, system_profiler). macOS keeps temperatures and fans behind the private SMC interface,
/// so those stay empty, as they do on Windows machines that do not expose them.
/// </summary>
public sealed class MacSensorReader : ISensorReader
{
    private (double? Health, DateTime AtUtc)? _health;

    public void Warmup()
    {
    }

    public SensorsInfo Read()
    {
        var battery = MacParsers.ParsePmsetBattery(Command.Run("pmset", "-g batt", 3000)?.Output ?? string.Empty);
        return new SensorsInfo
        {
            BatteryLevelPercent = battery?.Percent,
            BatteryStatus = battery?.Status,
            BatteryHealthPercent = battery is null ? null : BatteryHealth()
        };
    }

    /// <summary>"Maximum Capacity" from system_profiler; slow, so it is read every ten minutes.</summary>
    private double? BatteryHealth()
    {
        if (_health is { } cached && DateTime.UtcNow - cached.AtUtc < TimeSpan.FromMinutes(10))
        {
            return cached.Health;
        }

        double? health = null;
        using var document = MacParsers.Parse(Command.Run("system_profiler", "SPPowerDataType -json", 15_000)?.Output ?? string.Empty);
        if (document is not null && document.RootElement.TryGetProperty("SPPowerDataType", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("sppower_battery_health_info", out var info)
                    && MacParsers.Str(info, "sppower_battery_health_maximum_capacity") is { } capacity
                    && double.TryParse(capacity.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    health = percent;
                }
            }
        }

        _health = (health, DateTime.UtcNow);
        return health;
    }
}
