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

public sealed class RamInfo
{
    public double TotalGB { get; init; }

    public double UsedGB { get; init; }

    public double FreeGB { get; init; }

    public double CachedGB { get; init; }

    public double UsagePercent { get; init; }

    public double SwapTotalGB { get; init; }

    public double SwapUsedGB { get; init; }
}

public sealed class DiskPartition
{
    public string DriveLetter { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string FileSystem { get; init; } = string.Empty;

    public double TotalGB { get; init; }

    public double UsedGB { get; init; }

    public double FreeGB { get; init; }

    public double UsagePercent { get; init; }
}

public sealed class PhysicalDisk
{
    public int Index { get; init; }

    public string Model { get; init; } = "Unknown";

    public string Type { get; init; } = "Unknown";

    public string Interface { get; init; } = "Unknown";

    public double SizeGB { get; init; }

    public double? HealthPercent { get; init; }

    public double? TemperatureC { get; init; }

    public string SmartStatus { get; init; } = "Unknown";

    public List<string> Partitions { get; init; } = [];
}

public sealed class NetworkInfo
{
    public string ActiveInterface { get; init; } = "Unknown";

    public string Status { get; init; } = "Disconnected";

    public string IpAddress { get; init; } = string.Empty;

    public string MacAddress { get; init; } = string.Empty;

    public string Gateway { get; init; } = string.Empty;

    public string[] DnsServers { get; init; } = [];

    public double DownloadMbps { get; init; }

    public double UploadMbps { get; init; }

    public double TotalRxGB { get; init; }

    public double TotalTxGB { get; init; }

    public double? PingMs { get; init; }
}

public sealed class HardwareInfo
{
    public string Manufacturer { get; init; } = "Unknown";

    public string Model { get; init; } = "Unknown";

    public string SerialNumber { get; init; } = "Unknown";

    public string BiosVersion { get; init; } = "Unknown";

    public string Motherboard { get; init; } = "Unknown";

    public string GpuModel { get; init; } = "Unknown";

    public double GpuVramGB { get; init; }
}

public sealed class OsInfo
{
    public string Name { get; init; } = "Unknown";

    public string Version { get; init; } = "Unknown";

    public string Architecture { get; init; } = "Unknown";

    public DateTime? InstallDate { get; init; }

    public DateTime? LastBoot { get; init; }

    public TimeSpan Uptime { get; init; }

    public string Timezone { get; init; } = TimeZoneInfo.Local.DisplayName;
}

public sealed class ProcessInfo
{
    public string Name { get; init; } = string.Empty;

    public int Pid { get; init; }

    public double CpuPercent { get; init; }

    public double RamMB { get; init; }
}
