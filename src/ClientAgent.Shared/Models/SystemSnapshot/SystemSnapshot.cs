namespace ClientAgent.Shared.Models;

public sealed class SystemSnapshot
{
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public CpuInfo Cpu { get; init; } = new();

    public RamInfo Ram { get; init; } = new();

    public List<DiskPartition> Partitions { get; init; } = [];

    public List<PhysicalDisk> PhysicalDisks { get; init; } = [];

    public NetworkInfo Network { get; init; } = new();

    public HardwareInfo Hardware { get; init; } = new();

    public OsInfo Os { get; init; } = new();

    public List<ProcessInfo> TopProcesses { get; init; } = [];
}
