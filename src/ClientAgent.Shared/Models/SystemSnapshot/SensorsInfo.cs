namespace ClientAgent.Shared.Models;

public sealed class SensorsInfo
{
    public double? CpuTempC { get; init; }

    public double? GpuTempC { get; init; }

    public double? MotherboardTempC { get; init; }

    public double? CpuFanRpm { get; init; }

    public double? GpuFanRpm { get; init; }

    public double? CpuVoltage { get; init; }

    public double? Rail12V { get; init; }

    public double? Rail5V { get; init; }

    public double? Rail33V { get; init; }

    public double? PowerDrawW { get; init; }

    public double? BatteryLevelPercent { get; init; }

    public string? BatteryStatus { get; init; }

    public double? BatteryHealthPercent { get; init; }

    public double? DiskTempC { get; init; }

    public string? DiskHealth { get; init; }

    public string? DiskPowerOn { get; init; }
}
