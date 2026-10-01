using System.Diagnostics;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Mac;

/// <summary>Disk activity from the change in the IOBlockStorageDriver statistics (ioreg) between two readings.</summary>
public sealed class MacDiskActivityReader : IDiskActivityReader
{
    private BlockStats? _last;
    private long _lastAt;

    public bool Start()
    {
        _last = Read(out _lastAt);
        return _last is not null;
    }

    public DiskActivityDto Read()
    {
        var now = Read(out var at);
        if (now is null || _last is null)
        {
            return new DiskActivityDto();
        }

        var activity = Compare(_last, now, Stopwatch.GetElapsedTime(_lastAt, at).TotalMilliseconds);
        _last = now;
        _lastAt = at;
        return activity;
    }

    public static DiskActivityDto Compare(BlockStats before, BlockStats after, double elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return new DiskActivityDto();
        }

        var seconds = elapsedMs / 1000d;
        var operations = Math.Max(0, after.Operations - before.Operations);
        var busyMs = Math.Max(0, after.TotalTimeNs - before.TotalTimeNs) / 1_000_000d;
        return new DiskActivityDto
        {
            ActiveTimePercent = Math.Clamp(busyMs / elapsedMs * 100d, 0, 100),
            ReadBytesPerSecond = Math.Max(0, after.BytesRead - before.BytesRead) / seconds,
            WriteBytesPerSecond = Math.Max(0, after.BytesWritten - before.BytesWritten) / seconds,
            ResponseMs = operations > 0 ? busyMs / operations : 0,
            QueueLength = 0
        };
    }

    private static BlockStats? Read(out long at)
    {
        var output = Command.Run("ioreg", "-c IOBlockStorageDriver -r -w 0", 3000)?.Output;
        at = Stopwatch.GetTimestamp();
        return string.IsNullOrEmpty(output) ? null : MacParsers.ParseIoregStatistics(output);
    }
}
