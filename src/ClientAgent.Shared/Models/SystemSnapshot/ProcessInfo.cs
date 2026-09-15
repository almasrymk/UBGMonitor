namespace ClientAgent.Shared.Models;

public sealed class ProcessInfo
{
    public string Name { get; init; } = string.Empty;

    public int Pid { get; init; }

    public double CpuPercent { get; init; }

    public double RamMB { get; init; }
}
