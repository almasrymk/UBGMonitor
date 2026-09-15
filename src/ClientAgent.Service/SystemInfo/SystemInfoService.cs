using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.SystemInfo;

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
    private readonly HardwareMonitorReader _hardwareMonitor;
    private readonly object _networkLock = new();
    private Dictionary<int, ulong> _lastNetworkBytes = [];
    private long _lastNetworkTimestamp;

    public SystemInfoService(ILogger<SystemInfoService> logger, HardwareMonitorReader hardwareMonitor)
    {
        _logger = logger;
        _hardwareMonitor = hardwareMonitor;
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
        => Task.Run(GetNetworkInternal, cancellationToken);

    public Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetHardwareInternal, cancellationToken);

    public Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetOsInternal, cancellationToken);

    public Task<SensorsInfo> GetSensorsAsync(CancellationToken cancellationToken = default)
        => Task.Run(_hardwareMonitor.Read, cancellationToken);

    public Task<IReadOnlyList<ProcessInfo>> GetTopProcessesAsync(int count, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessInfo>>(() => GetTopProcessesInternal(count), cancellationToken);

    public Task<IReadOnlyList<ProcessTopDto>> GetTopProcessesSortedAsync(int count, string sortBy, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessTopDto>>(() => GetTopProcessesSortedInternal(count, sortBy), cancellationToken);

    private CpuInfo GetCpuInternal()
    {
        try
        {
            var model = QueryFirst("Win32_Processor", "Name") ?? "Unknown";
            var maxClock = ParseDouble(QueryFirst("Win32_Processor", "MaxClockSpeed"));
            var currentClock = ParseDouble(QueryFirst("Win32_Processor", "CurrentClockSpeed"));
            var physicalCores = ParseInt(QueryFirst("Win32_Processor", "NumberOfCores"), Environment.ProcessorCount);
            var logicalCores = ParseInt(QueryFirst("Win32_Processor", "NumberOfLogicalProcessors"), Environment.ProcessorCount);

            var usage = ReadCpuUsage();
            var perCore = ReadPerCoreUsage(logicalCores);
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
                TemperatureC = ReadCpuTemperature(),
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
            var totalBytes = ParseLong(QueryFirst("Win32_ComputerSystem", "TotalPhysicalMemory"));
            var freeKb = ParseLong(QueryFirst("Win32_OperatingSystem", "FreePhysicalMemory"));
            var totalSwapKb = ParseLong(QueryFirst("Win32_OperatingSystem", "TotalVirtualMemorySize"));
            var freeSwapKb = ParseLong(QueryFirst("Win32_OperatingSystem", "FreeVirtualMemory"));
            var cachedKb = ParseLong(QueryFirst("Win32_PerfFormattedData_PerfOS_Memory", "CacheBytes")) / 1024;

            var totalGb = BytesToGb(totalBytes);
            var freeGb = KbToGb(freeKb);
            var usedGb = Math.Max(0, totalGb - freeGb);
            var usage = totalGb <= 0 ? 0 : usedGb / totalGb * 100;
            var swapTotal = KbToGb(totalSwapKb);
            var swapUsed = Math.Max(0, swapTotal - KbToGb(freeSwapKb));

            return new RamInfo
            {
                TotalGB = Round(totalGb),
                UsedGB = Round(usedGb),
                FreeGB = Round(freeGb),
                CachedGB = Round(KbToGb(cachedKb)),
                UsagePercent = Math.Round(usage, 1),
                SwapTotalGB = Round(swapTotal),
                SwapUsedGB = Round(swapUsed)
            };
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
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
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
        var disks = new List<PhysicalDisk>();
        try
        {
            foreach (var obj in Query("Win32_DiskDrive"))
            {
                using (obj)
                {
                    var index = ParseInt(obj["Index"]?.ToString(), disks.Count);
                    disks.Add(new PhysicalDisk
                    {
                        Index = index,
                        Model = obj["Model"]?.ToString() ?? "Unknown",
                        Type = obj["MediaType"]?.ToString() ?? "Unknown",
                        Interface = obj["InterfaceType"]?.ToString() ?? "Unknown",
                        SizeGB = Round(BytesToGb(ParseLong(obj["Size"]?.ToString()))),
                        SmartStatus = obj["Status"]?.ToString() ?? "Unknown",
                        HealthPercent = ReadMsftHealth(index),
                        TemperatureC = null,
                        Partitions = GetPartitionLetters(index)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect physical disk info");
        }

        return disks;
    }

    private NetworkInfo GetNetworkInternal()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.GetIPStatistics().BytesReceived)
                .FirstOrDefault();

            if (nic is null)
            {
                return new NetworkInfo();
            }

            var props = nic.GetIPProperties();
            var ip = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.Address.ToString() ?? string.Empty;
            var gateway = props.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? string.Empty;
            var stats = nic.GetIPStatistics();

            return new NetworkInfo
            {
                ActiveInterface = nic.Name,
                Status = nic.OperationalStatus.ToString(),
                IpAddress = ip,
                MacAddress = FormatMac(nic.GetPhysicalAddress().ToString()),
                Gateway = gateway,
                DnsServers = props.DnsAddresses.Select(a => a.ToString()).ToArray(),
                DownloadMbps = nic.Speed > 0 ? Math.Round(nic.Speed / 1_000_000d, 1) : 0,
                UploadMbps = nic.Speed > 0 ? Math.Round(nic.Speed / 1_000_000d, 1) : 0,
                TotalRxGB = Round(BytesToGb(stats.BytesReceived)),
                TotalTxGB = Round(BytesToGb(stats.BytesSent)),
                PingMs = ProbeGateway(gateway)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect network info");
            return new NetworkInfo();
        }
    }

    private HardwareInfo GetHardwareInternal()
    {
        try
        {
            var cpuName = QueryFirst("Win32_Processor", "Name") ?? "Unknown";
            var cores = ParseInt(QueryFirst("Win32_Processor", "NumberOfCores"), Environment.ProcessorCount);
            var threads = ParseInt(QueryFirst("Win32_Processor", "NumberOfLogicalProcessors"), Environment.ProcessorCount);
            var maxClockMhz = ParseDouble(QueryFirst("Win32_Processor", "MaxClockSpeed"));
            var gpuModel = QueryFirst("Win32_VideoController", "Name") ?? "Unknown";
            var gpuVramGb = Round(BytesToGb(ParseLong(QueryFirst("Win32_VideoController", "AdapterRAM"))));
            var boardMfr = QueryFirst("Win32_BaseBoard", "Manufacturer") ?? string.Empty;
            var boardProduct = QueryFirst("Win32_BaseBoard", "Product") ?? "Unknown";
            var biosDate = ParseWmiDate(QueryFirst("Win32_BIOS", "ReleaseDate"));

            return new HardwareInfo
            {
                Manufacturer = QueryFirst("Win32_ComputerSystem", "Manufacturer") ?? "Unknown",
                Model = QueryFirst("Win32_ComputerSystem", "Model") ?? "Unknown",
                SerialNumber = QueryFirst("Win32_BIOS", "SerialNumber") ?? "Unknown",
                Cpu = cpuName.Trim(),
                CoresThreads = $"{cores} / {threads}",
                CpuSpeed = maxClockMhz > 0 ? $"{maxClockMhz / 1000:0.0} GHz" : "Unknown",
                Ram = FormatRamModules(),
                Gpu = gpuVramGb > 0 ? $"{gpuModel} ({gpuVramGb:0}GB)" : gpuModel,
                Disk = FormatPrimaryDisk(),
                Motherboard = string.IsNullOrWhiteSpace(boardMfr) ? boardProduct : $"{boardMfr} {boardProduct}".Trim(),
                BiosVersion = QueryFirst("Win32_BIOS", "SMBIOSBIOSVersion") ?? "Unknown",
                BiosDate = biosDate?.ToString("yyyy-MM-dd") ?? "Unknown",
                GpuModel = gpuModel,
                GpuVramGB = gpuVramGb
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect hardware info");
            return new HardwareInfo();
        }
    }

    private OsInfo GetOsInternal()
    {
        try
        {
            var lastBoot = ParseWmiDate(QueryFirst("Win32_OperatingSystem", "LastBootUpTime"));
            var installDate = ParseWmiDate(QueryFirst("Win32_OperatingSystem", "InstallDate"));
            return new OsInfo
            {
                Name = QueryFirst("Win32_OperatingSystem", "Caption") ?? Environment.OSVersion.ToString(),
                Version = QueryFirst("Win32_OperatingSystem", "Version") ?? Environment.OSVersion.Version.ToString(),
                Build = QueryFirst("Win32_OperatingSystem", "BuildNumber") ?? "Unknown",
                Architecture = QueryFirst("Win32_OperatingSystem", "OSArchitecture")
                               ?? (Environment.Is64BitOperatingSystem ? "x64" : "x86"),
                InstallDate = installDate,
                LastBoot = lastBoot,
                Uptime = lastBoot.HasValue ? DateTime.Now - lastBoot.Value : TimeSpan.Zero,
                Timezone = QueryFirst("Win32_TimeZone", "Caption") ?? TimeZoneInfo.Local.DisplayName,
                Locale = FormatLocale(QueryFirst("Win32_OperatingSystem", "Locale")),
                SystemType = QueryFirst("Win32_ComputerSystem", "SystemType") ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect OS info");
            return new OsInfo
            {
                Name = Environment.OSVersion.ToString(),
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86"
            };
        }
    }

    private List<ProcessInfo> GetTopProcessesInternal(int count)
    {
        try
        {
            return Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new ProcessInfo
                        {
                            Name = p.ProcessName,
                            Pid = p.Id,
                            CpuPercent = 0,
                            RamMB = Math.Round(p.WorkingSet64 / 1024d / 1024d, 1)
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(p => p is not null)
                .Cast<ProcessInfo>()
                .OrderByDescending(p => p.RamMB)
                .Take(Math.Max(1, count))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect process info");
            return [];
        }
    }

    private List<ProcessTopDto> GetTopProcessesSortedInternal(int count, string sortBy)
    {
        var take = Math.Max(1, count);
        var key = sortBy.ToLowerInvariant();
        try
        {
            return key switch
            {
                "cpu" => CollectByCpu(take),
                "network" => CollectByNetwork(take),
                _ => CollectByRam(take)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect sorted process info for {SortBy}", sortBy);
            return [];
        }
    }

    private List<ProcessTopDto> CollectByRam(int take)
    {
        return SnapshotProcesses()
            .OrderByDescending(row => row.RamMb)
            .Take(take)
            .Select(row => new ProcessTopDto
            {
                Name = row.Name,
                Pid = row.Pid,
                Value = row.RamMb,
                Unit = "MB"
            })
            .ToList();
    }

    private List<ProcessTopDto> CollectByCpu(int take)
    {
        var first = CaptureCpuTimes();
        Thread.Sleep(200);
        var cores = Math.Max(1, Environment.ProcessorCount);
        var rows = new List<(string Name, int Pid, double Cpu, double RamMb)>(first.Count);
        foreach (var (process, cpu0, ram) in first)
        {
            try
            {
                var deltaMs = (process.TotalProcessorTime - cpu0).TotalMilliseconds;
                var cpuPercent = Math.Clamp(deltaMs / (200d * cores) * 100d, 0, 100);
                rows.Add((process.ProcessName, process.Id, Math.Round(cpuPercent, 1), ram));
            }
            catch
            {
                // Process may have exited during sampling.
            }
            finally
            {
                process.Dispose();
            }
        }

        return rows
            .OrderByDescending(row => row.Cpu)
            .ThenByDescending(row => row.RamMb)
            .Take(take)
            .Select(row => new ProcessTopDto
            {
                Name = row.Name,
                Pid = row.Pid,
                Value = row.Cpu,
                Unit = "%"
            })
            .ToList();
    }

    private List<ProcessTopDto> CollectByNetwork(int take)
    {
        Dictionary<int, ulong> current = [];
        try
        {
            current = ProcessNetworkSampler.ReadBytesByPid(TimeSpan.FromMilliseconds(1200));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to sample process network usage");
        }

        var rates = new Dictionary<int, double>();
        lock (_networkLock)
        {
            var now = Stopwatch.GetTimestamp();
            if (_lastNetworkTimestamp > 0)
            {
                var elapsedSec = (now - _lastNetworkTimestamp) / (double)Stopwatch.Frequency;
                if (elapsedSec > 0.05)
                {
                    foreach (var (pid, bytes) in current)
                    {
                        var previous = _lastNetworkBytes.GetValueOrDefault(pid);
                        var delta = bytes > previous ? bytes - previous : 0;
                        rates[pid] = Math.Round(delta / elapsedSec / 1024d, 1);
                    }
                }
            }

            _lastNetworkBytes = current;
            _lastNetworkTimestamp = now;
        }

        return SnapshotProcesses()
            .Select(row => (row.Name, row.Pid, row.RamMb, Rate: rates.GetValueOrDefault(row.Pid)))
            .OrderByDescending(row => row.Rate)
            .ThenByDescending(row => row.RamMb)
            .Take(take)
            .Select(row => new ProcessTopDto
            {
                Name = row.Name,
                Pid = row.Pid,
                Value = row.Rate,
                Unit = "KB/s"
            })
            .ToList();
    }

    private static List<(Process Process, TimeSpan Cpu, double RamMb)> CaptureCpuTimes()
    {
        var samples = new List<(Process Process, TimeSpan Cpu, double RamMb)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                samples.Add((process, process.TotalProcessorTime, Math.Round(process.WorkingSet64 / 1024d / 1024d, 1)));
            }
            catch
            {
                process.Dispose();
            }
        }

        return samples;
    }

    private static List<(string Name, int Pid, double RamMb)> SnapshotProcesses()
    {
        var rows = new List<(string Name, int Pid, double RamMb)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                rows.Add((process.ProcessName, process.Id, Math.Round(process.WorkingSet64 / 1024d / 1024d, 1)));
            }
            catch
            {
                // Ignore processes that cannot be inspected.
            }
            finally
            {
                process.Dispose();
            }
        }

        return rows;
    }

    private static double ReadCpuUsage()
    {
        try
        {
            using var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = counter.NextValue();
            Thread.Sleep(200);
            return counter.NextValue();
        }
        catch
        {
            return 0;
        }
    }

    private static double[] ReadPerCoreUsage(int logicalCores)
    {
        try
        {
            var counters = Enumerable.Range(0, logicalCores)
                .Select(i => new PerformanceCounter("Processor", "% Processor Time", i.ToString()))
                .ToArray();
            foreach (var c in counters) { _ = c.NextValue(); }
            Thread.Sleep(200);
            var values = counters.Select(c => Math.Round((double)c.NextValue(), 1)).ToArray();
            foreach (var c in counters) { c.Dispose(); }
            return values;
        }
        catch
        {
            return [];
        }
    }

    private static double? ReadCpuTemperature()
    {
        try
        {
            foreach (var obj in Query("MSAcpi_ThermalZoneTemperature", @"root\WMI"))
            {
                using (obj)
                {
                    if (obj["CurrentTemperature"] is not null
                        && double.TryParse(obj["CurrentTemperature"].ToString(), out var tenthsKelvin))
                    {
                        return Math.Round(tenthsKelvin / 10d - 273.15, 1);
                    }
                }
            }
        }
        catch
        {
            // Temperature is frequently unavailable without admin rights.
        }

        return null;
    }

    private static double? ReadMsftHealth(int index)
    {
        try
        {
            foreach (var obj in Query("MSFT_PhysicalDisk", @"root\microsoft\windows\storage"))
            {
                using (obj)
                {
                    var deviceId = obj["DeviceId"]?.ToString();
                    if (deviceId == index.ToString() && obj["HealthStatus"] is not null)
                    {
                        return obj["HealthStatus"]?.ToString() switch
                        {
                            "0" => 100,
                            "1" => 70,
                            _ => 40
                        };
                    }
                }
            }
        }
        catch
        {
            // Storage namespace may be unavailable.
        }

        return null;
    }

    private static List<string> GetPartitionLetters(int diskIndex)
    {
        var letters = new List<string>();
        try
        {
            foreach (var partition in Query($"Win32_DiskPartition WHERE DiskIndex={diskIndex}"))
            {
                using (partition)
                {
                    var deviceId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        continue;
                    }

                    foreach (var logical in Query("Win32_LogicalDiskToPartition"))
                    {
                        using (logical)
                        {
                            var antecedent = logical["Antecedent"]?.ToString() ?? string.Empty;
                            if (antecedent.Contains(deviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                var dependent = logical["Dependent"]?.ToString() ?? string.Empty;
                                var start = dependent.IndexOf("DeviceID=", StringComparison.OrdinalIgnoreCase);
                                if (start >= 0)
                                {
                                    var letter = dependent[(start + 9)..].Trim('"', ' ', '\\');
                                    letters.Add(letter);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore mapping failures.
        }

        return letters;
    }

    private static double? ProbeGateway(string gateway)
    {
        if (string.IsNullOrWhiteSpace(gateway))
        {
            return null;
        }

        try
        {
            using var ping = new Ping();
            var reply = ping.Send(gateway, 1000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<ManagementObject> Query(string className, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, $"SELECT * FROM {className}");
        foreach (ManagementObject obj in searcher.Get())
        {
            yield return obj;
        }
    }

    private static string? QueryFirst(string className, string property)
    {
        try
        {
            foreach (var obj in Query(className))
            {
                using (obj)
                {
                    return obj[property]?.ToString();
                }
            }
        }
        catch
        {
            // Ignore WMI failures.
        }

        return null;
    }

    private static string FormatRamModules()
    {
        long totalBytes = 0;
        int speed = 0;
        int type = 0;
        try
        {
            foreach (var obj in Query("Win32_PhysicalMemory"))
            {
                using (obj)
                {
                    totalBytes += ParseLong(obj["Capacity"]?.ToString());
                    if (speed == 0)
                    {
                        speed = ParseInt(obj["Speed"]?.ToString(), 0);
                    }

                    if (type == 0)
                    {
                        type = ParseInt(obj["SMBIOSMemoryType"]?.ToString(), 0);
                    }
                }
            }
        }
        catch
        {
            // Fall back to computer-system total below.
        }

        if (totalBytes <= 0)
        {
            totalBytes = ParseLong(QueryFirst("Win32_ComputerSystem", "TotalPhysicalMemory"));
        }

        var gb = Math.Round(BytesToGb(totalBytes), 0);
        var kind = type switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            _ => "RAM"
        };
        return speed > 0 ? $"{gb:0} GB {kind} {speed}MHz" : $"{gb:0} GB {kind}";
    }

    private static string FormatPrimaryDisk()
    {
        try
        {
            foreach (var obj in Query("Win32_DiskDrive"))
            {
                using (obj)
                {
                    var model = obj["Model"]?.ToString() ?? "Unknown";
                    var sizeGb = Round(BytesToGb(ParseLong(obj["Size"]?.ToString())));
                    var iface = obj["InterfaceType"]?.ToString();
                    return string.IsNullOrWhiteSpace(iface)
                        ? $"{model} {sizeGb:0}GB"
                        : $"{model} {sizeGb:0}GB {iface}";
                }
            }
        }
        catch
        {
            // Ignore disk query failures.
        }

        return "Unknown";
    }

    private static string FormatLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "Unknown";
        }

        if (int.TryParse(locale, System.Globalization.NumberStyles.HexNumber, null, out var lcid))
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultureInfo(lcid).Name;
            }
            catch
            {
                // Keep the WMI locale code.
            }
        }

        return locale;
    }

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length != 12)
        {
            return raw;
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    private static DateTime? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 14)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch
        {
            return null;
        }
    }

    private static double BytesToGb(long bytes) => bytes / 1024d / 1024d / 1024d;

    private static double KbToGb(long kb) => kb / 1024d / 1024d;

    private static double Round(double value) => Math.Round(value, 2);

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value)
        => double.TryParse(value, out var parsed) ? parsed : 0;
}
