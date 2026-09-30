using System.Diagnostics;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.SystemInfo;

public interface IDiskActivityService
{
    /// <summary>The latest one-second reading, or null before the first reading or if the counters are unavailable.</summary>
    DiskActivityDto? GetActivity();

    /// <summary>The average of the readings since the last call, or null when there were none.</summary>
    DiskActivityDto? TakeAverage();
}

/// <summary>Reads the PhysicalDisk "_Total" performance counters once per second.</summary>
public sealed class DiskActivityService : BackgroundService, IDiskActivityService
{
    private const string Category = "PhysicalDisk";
    private const string Instance = "_Total";

    private readonly ILogger<DiskActivityService> _logger;
    private readonly object _sumLock = new();
    private DiskActivityDto? _latest;
    private int _count;
    private double _activeSum, _readSum, _writeSum, _responseSum, _queueSum;

    public DiskActivityService(ILogger<DiskActivityService> logger)
    {
        _logger = logger;
    }

    public DiskActivityDto? GetActivity() => Volatile.Read(ref _latest);

    public DiskActivityDto? TakeAverage()
    {
        lock (_sumLock)
        {
            if (_count == 0)
            {
                return null;
            }

            var average = new DiskActivityDto
            {
                ActiveTimePercent = _activeSum / _count,
                ReadBytesPerSecond = _readSum / _count,
                WriteBytesPerSecond = _writeSum / _count,
                ResponseMs = _responseSum / _count,
                QueueLength = _queueSum / _count
            };
            _count = 0;
            _activeSum = _readSum = _writeSum = _responseSum = _queueSum = 0;
            return average;
        }
    }

    private void Add(DiskActivityDto activity)
    {
        lock (_sumLock)
        {
            _count++;
            _activeSum += activity.ActiveTimePercent;
            _readSum += activity.ReadBytesPerSecond;
            _writeSum += activity.WriteBytesPerSecond;
            _responseSum += activity.ResponseMs;
            _queueSum += activity.QueueLength;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PerformanceCounter idle, read, write, transfer, queue;
        try
        {
            idle = new PerformanceCounter(Category, "% Idle Time", Instance, readOnly: true);
            read = new PerformanceCounter(Category, "Disk Read Bytes/sec", Instance, readOnly: true);
            write = new PerformanceCounter(Category, "Disk Write Bytes/sec", Instance, readOnly: true);
            transfer = new PerformanceCounter(Category, "Avg. Disk sec/Transfer", Instance, readOnly: true);
            queue = new PerformanceCounter(Category, "Current Disk Queue Length", Instance, readOnly: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Disk activity counters are unavailable");
            return;
        }

        using (idle)
        using (read)
        using (write)
        using (transfer)
        using (queue)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var activity = new DiskActivityDto
                    {
                        ActiveTimePercent = Math.Clamp(100d - idle.NextValue(), 0, 100),
                        ReadBytesPerSecond = Math.Max(0, read.NextValue()),
                        WriteBytesPerSecond = Math.Max(0, write.NextValue()),
                        ResponseMs = Math.Max(0, transfer.NextValue() * 1000d),
                        QueueLength = Math.Max(0, queue.NextValue())
                    };
                    Volatile.Write(ref _latest, activity);
                    Add(activity);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogDebug(ex, "Reading disk activity failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
