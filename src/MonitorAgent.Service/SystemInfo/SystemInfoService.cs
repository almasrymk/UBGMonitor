using System.Diagnostics;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.SystemInfo;

public interface ISystemInfoService
{
    Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<CpuInfo> GetCpuAsync(CancellationToken cancellationToken = default);

    Task<RamInfo> GetRamAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiskPartition>> GetPartitionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhysicalDisk>> GetPhysicalDisksAsync(CancellationToken cancellationToken = default);

    Task<NetworkInfo> GetNetworkAsync(CancellationToken cancellationToken = default);

    Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default);

    Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default);

    Task<SensorsInfo> GetSensorsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcessInfo>> GetTopProcessesAsync(int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProcessTopDto>> GetTopProcessesSortedAsync(int count, string sortBy, CancellationToken cancellationToken = default);
}

public sealed class SystemInfoService : ISystemInfoService
{
    private readonly ILogger<SystemInfoService> _logger;
    private readonly ISystemProbe _probe;
    private readonly IHardwareService _hardware;
    private readonly ISensorsService _sensors;
    private readonly INetworkService _network;
    private readonly ProcessUsageSampler _usage;
    private ProcessorDetails? _processor;

    public SystemInfoService(
        ILogger<SystemInfoService> logger,
        ISystemProbe probe,
        IHardwareService hardware,
        ISensorsService sensors,
        INetworkService network,
        ProcessUsageSampler usage)
    {
        _logger = logger;
        _probe = probe;
        _hardware = hardware;
        _sensors = sensors;
        _network = network;
        _usage = usage;
    }

    public Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var cpuTask = GetCpuAsync(cancellationToken);
            var ramTask = GetRamAsync(cancellationToken);
            var partitionsTask = GetPartitionsAsync(cancellationToken);
            var disksTask = GetPhysicalDisksAsync(cancellationToken);
            var networkTask = GetNetworkAsync(cancellationToken);
            var hardwareTask = GetHardwareAsync(cancellationToken);
            var osTask = GetOsAsync(cancellationToken);
            var processesTask = GetTopProcessesAsync(10, cancellationToken);

            await Task.WhenAll(cpuTask, ramTask, partitionsTask, disksTask, networkTask, hardwareTask, osTask, processesTask);

            return new SystemSnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                Cpu = await cpuTask,
                Ram = await ramTask,
                Partitions = [.. await partitionsTask],
                PhysicalDisks = [.. await disksTask],
                Network = await networkTask,
                Hardware = await hardwareTask,
                Os = await osTask,
                TopProcesses = [.. await processesTask]
            };
        }, cancellationToken);
    }

    public Task<CpuInfo> GetCpuAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetCpuInternal, cancellationToken);

    public Task<RamInfo> GetRamAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetRamInternal, cancellationToken);

    public Task<IReadOnlyList<DiskPartition>> GetPartitionsAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DiskPartition>>(GetPartitionsInternal, cancellationToken);

    public Task<IReadOnlyList<PhysicalDisk>> GetPhysicalDisksAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<PhysicalDisk>>(GetPhysicalDisksInternal, cancellationToken);

    public Task<NetworkInfo> GetNetworkAsync(CancellationToken cancellationToken = default)
        => _network.GetNetworkAsync(cancellationToken);

    public Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default)
        => _hardware.GetHardwareAsync(cancellationToken);

    public Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default)
        => _hardware.GetOsAsync(cancellationToken);

    public Task<SensorsInfo> GetSensorsAsync(CancellationToken cancellationToken = default)
        => _sensors.GetSensorsAsync(cancellationToken);

    public Task<IReadOnlyList<ProcessInfo>> GetTopProcessesAsync(int count, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessInfo>>(() => GetTopProcessesInternal(count), cancellationToken);

    public Task<IReadOnlyList<ProcessTopDto>> GetTopProcessesSortedAsync(int count, string sortBy, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessTopDto>>(() => GetTopProcessesSortedInternal(count, sortBy), cancellationToken);

    private CpuInfo GetCpuInternal()
    {
        try
        {
            var (model, maxClock, physicalCores, logicalCores) = _processor ??= _probe.ReadProcessor();
            var currentClock = _probe.ReadCurrentClockMhz();
            var (usage, perCore) = _probe.ReadCpuUsage(logicalCores);
            var processes = Process.GetProcesses();
            var threadCount = processes.Sum(p =>
            {
                try { return p.Threads.Count; }
                catch { return 0; }
            });

            return new CpuInfo
            {
                Model = model.Trim(),
                PhysicalCores = physicalCores,
                LogicalCores = logicalCores,
                UsagePercent = Math.Round(usage, 1),
                CurrentSpeedGhz = Math.Round(currentClock / 1000d, 2),
                MaxSpeedGhz = Math.Round(maxClock / 1000d, 2),
                TemperatureC = _probe.ReadCpuTemperature(),
                ProcessCount = processes.Length,
                ThreadCount = threadCount,
                PerCoreUsage = perCore
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect CPU info");
            return new CpuInfo { LogicalCores = Environment.ProcessorCount };
        }
    }

    private RamInfo GetRamInternal()
    {
        try
        {
            return _probe.ReadMemory();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect RAM info");
            return new RamInfo();
        }
    }

    private List<DiskPartition> GetPartitionsInternal()
    {
        try
        {
            return _probe.LocalDrives()
                .Select(d =>
                {
                    var total = BytesToGb(d.TotalSize);
                    var free = BytesToGb(d.TotalFreeSpace);
                    var used = Math.Max(0, total - free);
                    return new DiskPartition
                    {
                        DriveLetter = d.Name,
                        Label = d.VolumeLabel,
                        FileSystem = d.DriveFormat,
                        TotalGB = Round(total),
                        UsedGB = Round(used),
                        FreeGB = Round(free),
                        UsagePercent = total <= 0 ? 0 : Math.Round(used / total * 100, 1)
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect partition info");
            return [];
        }
    }

    private List<PhysicalDisk> GetPhysicalDisksInternal()
    {
        try
        {
            return _probe.ReadPhysicalDisks();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect physical disk info");
            return [];
        }
    }

    private List<ProcessInfo> GetTopProcessesInternal(int count)
        => _usage.Read().OrderByDescending(p => p.RamMB).Take(Math.Max(1, count))
            .Select(p => new ProcessInfo { Name = p.Name, Pid = p.Pid, CpuPercent = p.CpuPercent, RamMB = p.RamMB }).ToList();

    private List<ProcessTopDto> GetTopProcessesSortedInternal(int count, string sortBy)
    {
        var key = sortBy.ToLowerInvariant();
        double Value(ProcessUsage p) => key switch { "cpu" => p.CpuPercent, "network" => p.NetworkKBps, "disk" => p.DiskKBps, _ => p.RamMB };
        var unit = key == "cpu" ? "%" : key is "network" or "disk" ? "KB/s" : "MB";
        return _usage.Read().OrderByDescending(Value).ThenByDescending(p => p.RamMB).Take(Math.Max(1, count))
            .Select(p => new ProcessTopDto { Name = p.Name, Pid = p.Pid, Value = Math.Round(Value(p), 1), Unit = unit }).ToList();
    }
    private static double BytesToGb(long bytes) => Measure.BytesToGb(bytes);

    private static double Round(double value) => Measure.Round(value);
}
