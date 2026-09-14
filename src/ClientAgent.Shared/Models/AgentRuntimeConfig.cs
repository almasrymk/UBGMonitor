namespace ClientAgent.Shared.Models;

public sealed class AgentRuntimeConfig
{
    public string ConfigVersion { get; init; } = "1";

    public string AgentId { get; init; } = string.Empty;

    public List<MonitorPoint> MonitorPoints { get; init; } = [];

    public string? DatabaseConnectionString { get; init; }

    public double CpuCriticalThreshold { get; init; } = 90;

    public double RamCriticalThreshold { get; init; } = 90;

    public double DiskCriticalThreshold { get; init; } = 90;
}
