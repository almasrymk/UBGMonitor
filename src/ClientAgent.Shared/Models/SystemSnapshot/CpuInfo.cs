namespace ClientAgent.Shared.Models;

public sealed class CpuInfo
{
    public string Model { get; init; } = "Unknown";

    public int PhysicalCores { get; init; }

    public int LogicalCores { get; init; }

    public double UsagePercent { get; init; }

    public double CurrentSpeedGhz { get; init; }

    public double MaxSpeedGhz { get; init; }

    public double? TemperatureC { get; init; }

    public int ProcessCount { get; init; }

    public int ThreadCount { get; init; }

    public double[] PerCoreUsage { get; init; } = [];
}
