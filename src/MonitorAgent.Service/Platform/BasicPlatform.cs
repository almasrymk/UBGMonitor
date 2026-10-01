using System.Globalization;
using System.Runtime.InteropServices;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>What .NET itself can tell on any system; used where the system has no reader of its own yet.</summary>
public class BasicSystemProbe : ISystemProbe
{
    public virtual ProcessorDetails ReadProcessor()
        => new("Unknown", 0, Environment.ProcessorCount, Environment.ProcessorCount);

    public virtual double ReadCurrentClockMhz() => 0;

    public virtual (double Total, double[] PerCore) ReadCpuUsage(int logicalCores) => (0, []);

    public virtual double? ReadCpuTemperature() => null;

    public virtual RamInfo ReadMemory()
    {
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return new RamInfo { TotalGB = Measure.Round(Measure.BytesToGb(total)) };
    }

    public virtual IEnumerable<DriveInfo> LocalDrives()
        => DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);

    public virtual List<PhysicalDisk> ReadPhysicalDisks() => [];

    public virtual Dictionary<int, ulong> ReadProcessDiskBytes() => [];

    public virtual Dictionary<int, ulong> ReadProcessNetworkBytes(TimeSpan budget) => [];
}

public class BasicInventoryCollector : IInventoryCollector
{
    public static string Family => OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "System";

    public virtual HardwareInventory Collect()
    {
        var cores = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        return new HardwareInventory
        {
            OsFamily = Family,
            CpuCores = cores,
            CpuThreads = cores,
            CoresThreads = $"{cores} / {cores}",
            LastBoot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
            Timezone = TimeZoneInfo.Local.DisplayName,
            Locale = CultureInfo.CurrentCulture.Name is { Length: > 0 } name ? name : "-",
            OsEdition = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.Version.ToString(),
            OsBuild = "-",
            Architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            SystemType = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };
    }
}

public sealed class NoSensorReader : ISensorReader
{
    public void Warmup()
    {
    }

    public SensorsInfo Read() => new();
}

public sealed class NoDiskActivityReader : IDiskActivityReader
{
    public bool Start() => false;

    public DiskActivityDto Read() => new();
}
