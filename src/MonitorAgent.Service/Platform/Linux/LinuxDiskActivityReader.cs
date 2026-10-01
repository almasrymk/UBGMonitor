using System.Diagnostics;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>Disk activity from the changes in /proc/diskstats between two readings, over all physical disks.</summary>
public sealed class LinuxDiskActivityReader : IDiskActivityReader
{
    private List<DiskStat>? _last;
    private long _lastAt;

    public bool Start()
    {
        _last = LinuxParsers.ParseDiskStats(LinuxFiles.Read("/proc/diskstats"));
        _lastAt = Stopwatch.GetTimestamp();
        return _last.Count > 0;
    }

    public DiskActivityDto Read()
    {
        var now = LinuxParsers.ParseDiskStats(LinuxFiles.Read("/proc/diskstats"));
        var at = Stopwatch.GetTimestamp();
        var activity = Compare(_last ?? now, now, Stopwatch.GetElapsedTime(_lastAt, at).TotalMilliseconds);
        _last = now;
        _lastAt = at;
        return activity;
    }

    public static DiskActivityDto Compare(IReadOnlyList<DiskStat> before, IReadOnlyList<DiskStat> after, double elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return new DiskActivityDto();
        }

        double busy = 0, read = 0, written = 0, ios = 0, ioMs = 0, queue = 0;
        var disks = 0;
        foreach (var disk in after)
        {
            var previous = before.FirstOrDefault(d => d.Name == disk.Name);
            if (previous is null)
            {
                continue;
            }

            disks++;
            busy += Math.Max(0, disk.BusyMs - previous.BusyMs);
            read += Math.Max(0, disk.SectorsRead - previous.SectorsRead);
            written += Math.Max(0, disk.SectorsWritten - previous.SectorsWritten);
            ios += Math.Max(0, disk.Ios - previous.Ios);
            ioMs += Math.Max(0, disk.IoMs - previous.IoMs);
            queue += disk.InFlight;
        }

        var seconds = elapsedMs / 1000d;
        return new DiskActivityDto
        {
            // Like the Windows "_Total": the average busy time of the disks.
            ActiveTimePercent = disks == 0 ? 0 : Math.Clamp(busy / disks / elapsedMs * 100d, 0, 100),
            ReadBytesPerSecond = read * 512 / seconds,
            WriteBytesPerSecond = written * 512 / seconds,
            ResponseMs = ios > 0 ? ioMs / ios : 0,
            QueueLength = queue
        };
    }
}
