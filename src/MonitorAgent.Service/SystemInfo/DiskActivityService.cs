using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.SystemInfo;

public interface IDiskActivityService
{
    /// <summary>The latest one-second reading, or null before the first reading or if the counters are unavailable.</summary>
    DiskActivityDto? GetActivity();

    /// <summary>The average of the readings since the last call, or null when there were none.</summary>
    DiskActivityDto? TakeAverage();
}

/// <summary>Reads the activity of all disks once per second.</summary>
public sealed class DiskActivityService : BackgroundService, IDiskActivityService
{
    private readonly IDiskActivityReader _reader;
    private readonly ILogger<DiskActivityService> _logger;
    private readonly object _sumLock = new();
    private DiskActivityDto? _latest;
    private int _count;
    private double _activeSum, _readSum, _writeSum, _responseSum, _queueSum;

    public DiskActivityService(IDiskActivityReader reader, ILogger<DiskActivityService> logger)
    {
        _reader = reader;
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
        if (!_reader.Start())
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var activity = _reader.Read();
                Volatile.Write(ref _latest, activity);
                Add(activity);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                _logger.LogDebug(ex, "Reading disk activity failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
