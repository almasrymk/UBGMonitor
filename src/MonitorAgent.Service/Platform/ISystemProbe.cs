using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>The readings of this computer that each operating system exposes in its own way.</summary>
public interface ISystemProbe
{
    /// <summary>The processor's fixed details; called once.</summary>
    ProcessorDetails ReadProcessor();

    double ReadCurrentClockMhz();

    /// <summary>Total and per-core usage in percent. Readings less than half a second apart may share the last one.</summary>
    (double Total, double[] PerCore) ReadCpuUsage(int logicalCores);

    double? ReadCpuTemperature();

    RamInfo ReadMemory();

    /// <summary>The local disks (no network, optical, memory or system-internal file systems).</summary>
    IEnumerable<DriveInfo> LocalDrives();

    List<PhysicalDisk> ReadPhysicalDisks();

    /// <summary>Read plus write bytes per process since it started.</summary>
    Dictionary<int, ulong> ReadProcessDiskBytes();

    /// <summary>Bytes sent plus received per process so far; empty when the system does not count them per process.</summary>
    Dictionary<int, ulong> ReadProcessNetworkBytes(TimeSpan budget);
}

public sealed record ProcessorDetails(string Model, double MaxClockMhz, int PhysicalCores, int LogicalCores);
