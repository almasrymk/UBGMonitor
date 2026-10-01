using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemProbe : ISystemProbe, IDisposable
{
    private readonly object _cpuLock = new();
    private PerformanceCounter? _cpuTotal;
    private PerformanceCounter[] _cpuCores = [];
    private (double Total, double[] PerCore) _cpuSample = (0, []);
    private long _cpuSampledAt;
    private bool _cpuTemperatureUnavailable;

    /// <summary>The processor's fixed details: one WMI query each is the slow part of reading the CPU.</summary>
    public ProcessorDetails ReadProcessor() => new(
        (QueryFirst("Win32_Processor", "Name") ?? "Unknown").Trim(),
        ParseDouble(QueryFirst("Win32_Processor", "MaxClockSpeed")),
        ParseInt(QueryFirst("Win32_Processor", "NumberOfCores"), Environment.ProcessorCount),
        ParseInt(QueryFirst("Win32_Processor", "NumberOfLogicalProcessors"), Environment.ProcessorCount));

    public double ReadCurrentClockMhz() => ParseDouble(QueryFirst("Win32_Processor", "CurrentClockSpeed"));

    /// <summary>
    /// Total and per-core usage from counters that stay open, so each reading is the average since the one before.
    /// Readings less than half a second apart (the app and the notification checks) share the last one.
    /// </summary>
    public (double Total, double[] PerCore) ReadCpuUsage(int logicalCores)
    {
        lock (_cpuLock)
        {
            if (_cpuTotal is not null && Stopwatch.GetElapsedTime(_cpuSampledAt) < TimeSpan.FromMilliseconds(500))
            {
                return _cpuSample;
            }

            try
            {
                if (_cpuTotal is null)
                {
                    _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCores = Enumerable.Range(0, logicalCores)
                        .Select(i => new PerformanceCounter("Processor", "% Processor Time", i.ToString()))
                        .ToArray();
                    _ = _cpuTotal.NextValue();
                    foreach (var core in _cpuCores)
                    {
                        _ = core.NextValue();
                    }

                    Thread.Sleep(200);
                }

                _cpuSample = (_cpuTotal.NextValue(), _cpuCores.Select(core => Math.Round((double)core.NextValue(), 1)).ToArray());
                _cpuSampledAt = Stopwatch.GetTimestamp();
                return _cpuSample;
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                DisposeCounters();
                return (0, []);
            }
        }
    }

    public double? ReadCpuTemperature()
    {
        if (_cpuTemperatureUnavailable)
        {
            return null;
        }

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

        // Most devices do not expose it; asking again every second only slows down reading the CPU.
        _cpuTemperatureUnavailable = true;
        return null;
    }

    public RamInfo ReadMemory()
    {
        var totalBytes = ParseLong(QueryFirst("Win32_ComputerSystem", "TotalPhysicalMemory"));
        var freeKb = ParseLong(QueryFirst("Win32_OperatingSystem", "FreePhysicalMemory"));
        var totalSwapKb = ParseLong(QueryFirst("Win32_OperatingSystem", "TotalVirtualMemorySize"));
        var freeSwapKb = ParseLong(QueryFirst("Win32_OperatingSystem", "FreeVirtualMemory"));
        var cachedKb = ParseLong(QueryFirst("Win32_PerfFormattedData_PerfOS_Memory", "CacheBytes")) / 1024;
        return Measure.Ram(totalBytes, freeKb * 1024, cachedKb * 1024, totalSwapKb * 1024, freeSwapKb * 1024);
    }

    public IEnumerable<DriveInfo> LocalDrives()
        => DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);

    public List<PhysicalDisk> ReadPhysicalDisks()
    {
        var disks = new List<PhysicalDisk>();
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
                    SizeGB = Measure.Round(Measure.BytesToGb(ParseLong(obj["Size"]?.ToString()))),
                    SmartStatus = obj["Status"]?.ToString() ?? "Unknown",
                    HealthPercent = ReadMsftHealth(index),
                    TemperatureC = null,
                    Partitions = GetPartitionLetters(index)
                });
            }
        }

        return disks;
    }

    public Dictionary<int, ulong> ReadProcessDiskBytes() => ProcessDiskSampler.ReadBytesByPid();

    public Dictionary<int, ulong> ReadProcessNetworkBytes(TimeSpan budget) => ProcessNetworkSampler.ReadBytesByPid(budget);

    public void Dispose()
    {
        lock (_cpuLock)
        {
            DisposeCounters();
        }
    }

    private void DisposeCounters()
    {
        _cpuTotal?.Dispose();
        foreach (var core in _cpuCores)
        {
            core.Dispose();
        }

        _cpuTotal = null;
        _cpuCores = [];
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

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value)
        => double.TryParse(value, out var parsed) ? parsed : 0;
}
