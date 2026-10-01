using System.Diagnostics;
using System.Runtime.Versioning;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Mac;

[SupportedOSPlatform("macos")]
public sealed class MacSystemProbe : BasicSystemProbe
{
    /// <summary>Volumes macOS mounts for itself (the sealed system, recovery, VM swap...), not user storage.</summary>
    private static readonly string[] SystemVolumes =
    [
        "/System/Volumes/VM", "/System/Volumes/Preboot", "/System/Volumes/Update", "/System/Volumes/xarts",
        "/System/Volumes/iSCPreboot", "/System/Volumes/Hardware", "/private/var/vm", "/Library/Developer/CoreSimulator"
    ];

    private static readonly HashSet<string> DataFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "apfs", "hfs", "exfat", "msdos", "ntfs", "smbfs"
    };

    private readonly object _cpuLock = new();
    private List<(long Busy, long Total)>? _lastTicks;
    private (double Total, double[] PerCore) _cpuSample = (0, []);
    private long _cpuSampledAt;
    private (List<PhysicalDisk> Disks, DateTime AtUtc)? _disks;

    public override ProcessorDetails ReadProcessor()
    {
        var model = MacNative.SysctlString("machdep.cpu.brand_string") ?? "Apple Silicon";
        var maxHz = MacNative.SysctlLong("hw.cpufrequency_max") ?? 0;
        var logical = (int)(MacNative.SysctlLong("hw.logicalcpu") ?? Environment.ProcessorCount);
        var physical = (int)(MacNative.SysctlLong("hw.physicalcpu") ?? logical);
        return new ProcessorDetails(model, maxHz / 1_000_000d, physical, logical);
    }

    // Apple Silicon does not publish its clock speed; Intel Macs report the nominal one.
    public override double ReadCurrentClockMhz() => (MacNative.SysctlLong("hw.cpufrequency") ?? 0) / 1_000_000d;

    public override (double Total, double[] PerCore) ReadCpuUsage(int logicalCores)
    {
        lock (_cpuLock)
        {
            if (_lastTicks is not null && Stopwatch.GetElapsedTime(_cpuSampledAt) < TimeSpan.FromMilliseconds(500))
            {
                return _cpuSample;
            }

            if (_lastTicks is null)
            {
                _lastTicks = MacNative.CoreTicks();
                Thread.Sleep(200);
            }

            var now = MacNative.CoreTicks();
            long busy = 0, total = 0;
            var perCore = new double[now.Count];
            for (var i = 0; i < now.Count && i < _lastTicks.Count; i++)
            {
                var deltaBusy = now[i].Busy - _lastTicks[i].Busy;
                var deltaTotal = now[i].Total - _lastTicks[i].Total;
                busy += deltaBusy;
                total += deltaTotal;
                perCore[i] = deltaTotal <= 0 ? 0 : Math.Round(Math.Clamp(deltaBusy * 100d / deltaTotal, 0, 100), 1);
            }

            _lastTicks = now;
            _cpuSample = (total <= 0 ? 0 : Math.Clamp(busy * 100d / total, 0, 100), perCore);
            _cpuSampledAt = Stopwatch.GetTimestamp();
            return _cpuSample;
        }
    }

    public override RamInfo ReadMemory()
    {
        var total = MacNative.SysctlLong("hw.memsize") ?? 0;
        var vm = MacParsers.ParseVmStat(Command.Run("vm_stat", string.Empty, 3000)?.Output ?? string.Empty);
        // Like Activity Monitor: free, inactive, speculative and purgeable pages can be handed out at once.
        var available = (vm.Free + vm.Inactive + vm.Speculative + vm.Purgeable) * vm.PageSize;
        var (swapTotal, swapFree) = MacParsers.ParseSwapUsage(MacNative.SysctlString("vm.swapusage"));
        return Measure.Ram(total, Math.Min(available, total), vm.FileBacked * vm.PageSize, swapTotal, swapFree);
    }

    /// <summary>The startup disk once (the system and data volumes share its space) and every other volume.</summary>
    public override IEnumerable<DriveInfo> LocalDrives()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d =>
            {
                try
                {
                    return d.IsReady
                           && DataFileSystems.Contains(d.DriveFormat)
                           && !SystemVolumes.Any(prefix => d.Name.StartsWith(prefix, StringComparison.Ordinal))
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

    /// <summary>system_profiler takes a second or two, so the list is kept for five minutes.</summary>
    public override List<PhysicalDisk> ReadPhysicalDisks()
    {
        if (_disks is { } cached && DateTime.UtcNow - cached.AtUtc < TimeSpan.FromMinutes(5))
        {
            return cached.Disks;
        }

        var json = Command.Run("system_profiler", "SPNVMeDataType SPSerialATADataType -json", 20_000)?.Output ?? string.Empty;
        var disks = MacParsers.ParseStorage(json)
            .Select((disk, index) => new PhysicalDisk
            {
                Index = index,
                Model = disk.Model,
                Type = disk.Ssd ? "SSD" : "HDD",
                Interface = disk.Interface,
                SizeGB = Measure.Round(Measure.BytesToGb(disk.SizeBytes)),
                SmartStatus = disk.Smart == "Verified" ? "OK" : disk.Smart,
                Partitions = []
            })
            .ToList();
        _disks = (disks, DateTime.UtcNow);
        return disks;
    }

    public override Dictionary<int, ulong> ReadProcessDiskBytes()
    {
        var result = new Dictionary<int, ulong>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (MacNative.DiskBytes(process.Id) is ulong bytes)
                {
                    result[process.Id] = bytes;
                }
            }
        }

        return result;
    }

    public override Dictionary<int, ulong> ReadProcessNetworkBytes(TimeSpan budget)
        => MacParsers.ParseNettop(Command.Run("nettop", "-P -L 1 -x -J bytes_in,bytes_out", (int)Math.Max(1000, budget.TotalMilliseconds * 2))?.Output ?? string.Empty);
}
