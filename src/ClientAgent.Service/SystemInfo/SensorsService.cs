using ClientAgent.Shared.Models;

namespace ClientAgent.Service.SystemInfo;

public interface ISensorsService
{
    Task<SensorsInfo> GetSensorsAsync(CancellationToken cancellationToken = default);

    Task<HardwareLevelDto> GetLevelAsync(CancellationToken cancellationToken = default);
}

public sealed class SensorsService : ISensorsService
{
    private readonly HardwareMonitorReader _reader;

    public SensorsService(HardwareMonitorReader reader)
    {
        _reader = reader;
        _reader.Warmup();
    }

    public Task<SensorsInfo> GetSensorsAsync(CancellationToken cancellationToken = default)
        => Task.Run(_reader.Read, cancellationToken);

    public Task<HardwareLevelDto> GetLevelAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => MapLevel(_reader.Read()), cancellationToken);

    private static HardwareLevelDto MapLevel(SensorsInfo sensors)
    {
        var items = new List<HardwareItemDto>
        {
            Item("CPU Temp", FormatTemp(sensors.CpuTempC), TempStatus(sensors.CpuTempC)),
            Item("GPU Temp", FormatTemp(sensors.GpuTempC), TempStatus(sensors.GpuTempC)),
            Item("Motherboard Temp", FormatTemp(sensors.MotherboardTempC), TempStatus(sensors.MotherboardTempC)),
            Item("CPU Fan", FormatFan(sensors.CpuFanRpm), FanStatus(sensors.CpuFanRpm)),
            Item("GPU Fan", FormatFan(sensors.GpuFanRpm), FanStatus(sensors.GpuFanRpm)),
            Item("CPU Voltage", FormatVolt(sensors.CpuVoltage), VoltageStatus(sensors.CpuVoltage, 1.2)),
            Item("+12V Rail", FormatVolt(sensors.Rail12V), VoltageStatus(sensors.Rail12V, 12)),
            Item("+5V Rail", FormatVolt(sensors.Rail5V), VoltageStatus(sensors.Rail5V, 5)),
            Item("+3.3V Rail", FormatVolt(sensors.Rail33V), VoltageStatus(sensors.Rail33V, 3.3)),
            Item("Power Draw", FormatPower(sensors.PowerDrawW), PowerStatus(sensors.PowerDrawW)),
            Item("Battery Level", FormatPercent(sensors.BatteryLevelPercent), BatteryLevelStatus(sensors.BatteryLevelPercent)),
            Item("Battery Status", sensors.BatteryStatus ?? "-", BatteryStatusColor(sensors.BatteryStatus)),
            Item("Battery Health", FormatPercent(sensors.BatteryHealthPercent), BatteryHealthStatus(sensors.BatteryHealthPercent)),
            Item("Disk Temp", FormatTemp(sensors.DiskTempC), TempStatus(sensors.DiskTempC)),
            Item("Disk Health", sensors.DiskHealth ?? "-", PresenceStatus(sensors.DiskHealth)),
            Item("Disk Power-On", sensors.DiskPowerOn ?? "-", PresenceStatus(sensors.DiskPowerOn))
        };

        return new HardwareLevelDto
        {
            Level = 3,
            Title = "Live Sensors",
            Items = items,
            ItemCount = items.Count
        };
    }

    private static HardwareItemDto Item(string name, string value, string status)
        => new() { Name = name, Value = value, Status = status };

    private static string FormatTemp(double? value) => value is null ? "-" : $"{value:0}°C";

    private static string FormatFan(double? value) => value is null ? "-" : $"{value:0} RPM";

    private static string FormatVolt(double? value) => value is null ? "-" : $"{value:0.00} V";

    private static string FormatPercent(double? value) => value is null ? "-" : $"{value:0}%";

    private static string FormatPower(double? value) => value is null ? "-" : $"{value:0} W";

    private static string TempStatus(double? c)
    {
        if (c is null)
        {
            return "Red";
        }

        if (c < 70)
        {
            return "Green";
        }

        return c <= 85 ? "Yellow" : "Red";
    }

    private static string FanStatus(double? rpm)
    {
        if (rpm is null)
        {
            return "Red";
        }

        return rpm > 0 ? "Green" : "Yellow";
    }

    private static string VoltageStatus(double? value, double nominal)
    {
        if (value is null || nominal <= 0)
        {
            return "Red";
        }

        var delta = Math.Abs(value.Value - nominal) / nominal * 100;
        if (delta <= 5)
        {
            return "Green";
        }

        return delta <= 10 ? "Yellow" : "Red";
    }

    private static string PowerStatus(double? watts)
    {
        if (watts is null)
        {
            return "Red";
        }

        return watts > 0 ? "Green" : "Yellow";
    }

    private static string BatteryLevelStatus(double? percent)
    {
        if (percent is null)
        {
            return "Red";
        }

        if (percent > 50)
        {
            return "Green";
        }

        return percent >= 20 ? "Yellow" : "Red";
    }

    private static string BatteryHealthStatus(double? percent)
    {
        if (percent is null)
        {
            return "Red";
        }

        if (percent > 80)
        {
            return "Green";
        }

        return percent >= 50 ? "Yellow" : "Red";
    }

    private static string BatteryStatusColor(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status == "-")
        {
            return "Red";
        }

        if (status.Contains("Critical", StringComparison.OrdinalIgnoreCase))
        {
            return "Red";
        }

        if (status.Contains("Low", StringComparison.OrdinalIgnoreCase))
        {
            return "Yellow";
        }

        return "Green";
    }

    private static string PresenceStatus(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "-" ? "Red" : "Green";
}
