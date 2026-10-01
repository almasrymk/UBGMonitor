using System.Diagnostics;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Linux;

public sealed class LinuxSystemProbe : BasicSystemProbe
{
    private readonly object _cpuLock = new();
    private Dictionary<string, CpuTimes>? _lastTimes;
    private (double Total, double[] PerCore) _cpuSample = (0, []);
    private long _cpuSampledAt;

    public override ProcessorDetails ReadProcessor()
    {
        var info = LinuxParsers.ParseCpuInfo(LinuxFiles.Read("/proc/cpuinfo"));
        var maxKhz = LinuxFiles.ReadLong("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
        return new ProcessorDetails(info.Model, maxKhz is > 0 ? maxKhz.Value / 1000d : info.AverageMhz, info.PhysicalCores, info.LogicalCores);
    }

    public override double ReadCurrentClockMhz()
    {
        var average = LinuxParsers.ParseCpuInfo(LinuxFiles.Read("/proc/cpuinfo")).AverageMhz;
        if (average > 0)
        {
            return average;
        }

        // ARM boards do not list "cpu MHz"; cpufreq has it in kHz.
        var khz = LinuxFiles.ReadLong("/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq");
        return khz is > 0 ? khz.Value / 1000d : 0;
    }

    /// <summary>Usage since the previous reading (like the Windows counters); readings less than half a second apart share the last one.</summary>
    public override (double Total, double[] PerCore) ReadCpuUsage(int logicalCores)
    {
        lock (_cpuLock)
        {
            if (_lastTimes is not null && Stopwatch.GetElapsedTime(_cpuSampledAt) < TimeSpan.FromMilliseconds(500))
            {
                return _cpuSample;
            }

            if (_lastTimes is null)
            {
                _lastTimes = LinuxParsers.ParseProcStat(LinuxFiles.Read("/proc/stat"));
                Thread.Sleep(200);
            }

            var now = LinuxParsers.ParseProcStat(LinuxFiles.Read("/proc/stat"));
            var total = now.TryGetValue("cpu", out var after) && _lastTimes.TryGetValue("cpu", out var before)
                ? LinuxParsers.UsagePercent(before, after)
                : 0;
            var perCore = Enumerable.Range(0, logicalCores)
                .Select(i => now.TryGetValue($"cpu{i}", out var a) && _lastTimes.TryGetValue($"cpu{i}", out var b)
                    ? Math.Round(LinuxParsers.UsagePercent(b, a), 1)
                    : 0)
                .ToArray();
            _lastTimes = now;
            _cpuSample = (total, perCore);
            _cpuSampledAt = Stopwatch.GetTimestamp();
            return _cpuSample;
        }
    }

    public override double? ReadCpuTemperature() => LinuxHwmon.CpuTemperature(LinuxHwmon.Chips());

    public override RamInfo ReadMemory()
    {
        var memory = LinuxParsers.ParseMemInfo(LinuxFiles.Read("/proc/meminfo"));
        var total = memory.GetValueOrDefault("MemTotal");
        // MemAvailable counts the cache the kernel would give back, which matches "free" in the other systems.
        var available = memory.TryGetValue("MemAvailable", out var value)
            ? value
            : memory.GetValueOrDefault("MemFree") + memory.GetValueOrDefault("Cached") + memory.GetValueOrDefault("Buffers");
        var cached = memory.GetValueOrDefault("Cached") + memory.GetValueOrDefault("Buffers") + memory.GetValueOrDefault("SReclaimable");
        return Measure.Ram(total, available, cached, memory.GetValueOrDefault("SwapTotal"), memory.GetValueOrDefault("SwapFree"));
    }

    /// <summary>
    /// Mounted data file systems, without snaps and read-only images. A file system mounted in several places
    /// (btrfs subvolumes, bind mounts) is listed once, at its shortest path.
    /// </summary>
    public override IEnumerable<DriveInfo> LocalDrives()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d =>
            {
                try
                {
                    return d.IsReady
                           && LinuxDisks.DataFileSystems.Contains(d.DriveFormat)
                           && !d.Name.StartsWith("/snap/", StringComparison.Ordinal)
                           && d.TotalSize > 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            })
            .OrderBy(d => d.Name.Length);

        var seen = new HashSet<(long, long)>();
        foreach (var drive in drives)
        {
            if (seen.Add((drive.TotalSize, drive.TotalFreeSpace)))
            {
                yield return drive;
            }
        }
    }

    public override List<PhysicalDisk> ReadPhysicalDisks() => LinuxDisks.Read();

    /// <summary>Bytes each process read and wrote on disk (/proc/[pid]/io; other users' processes need root, which the service has).</summary>
    public override Dictionary<int, ulong> ReadProcessDiskBytes()
    {
        var result = new Dictionary<int, ulong>();
        foreach (var folder in LinuxFiles.Directories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(folder), out var pid))
            {
                continue;
            }

            var io = LinuxParsers.ParseMemInfo(LinuxFiles.Read($"{folder}/io"));
            if (io.TryGetValue("read_bytes", out var read) | io.TryGetValue("write_bytes", out var write))
            {
                result[pid] = (ulong)Math.Max(0, read + write);
            }
        }

        return result;
    }
}
