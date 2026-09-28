using System.Diagnostics;

namespace ClientAgent.UI.Services;

public readonly record struct DiskActivity(
    double ActiveTimePercent,
    double ReadBytesPerSecond,
    double WriteBytesPerSecond,
    double ResponseMs,
    double QueueLength);

public sealed class DiskActivitySampler : IDisposable
{
    private const string Category = "PhysicalDisk";
    private const string Instance = "_Total";

    private PerformanceCounter? _idle;
    private PerformanceCounter? _read;
    private PerformanceCounter? _write;
    private PerformanceCounter? _transfer;
    private PerformanceCounter? _queue;
    private bool _failed;

    public DiskActivity? Sample()
    {
        if (_failed)
        {
            return null;
        }

        try
        {
            if (_idle is null)
            {
                _idle = new PerformanceCounter(Category, "% Idle Time", Instance, readOnly: true);
                _read = new PerformanceCounter(Category, "Disk Read Bytes/sec", Instance, readOnly: true);
                _write = new PerformanceCounter(Category, "Disk Write Bytes/sec", Instance, readOnly: true);
                _transfer = new PerformanceCounter(Category, "Avg. Disk sec/Transfer", Instance, readOnly: true);
                _queue = new PerformanceCounter(Category, "Current Disk Queue Length", Instance, readOnly: true);
                _idle.NextValue();
                _read.NextValue();
                _write.NextValue();
                _transfer.NextValue();
                return null;
            }

            return new DiskActivity(
                Math.Clamp(100d - _idle.NextValue(), 0, 100),
                Math.Max(0, _read!.NextValue()),
                Math.Max(0, _write!.NextValue()),
                Math.Max(0, _transfer!.NextValue() * 1000d),
                Math.Max(0, _queue!.NextValue()));
        }
        catch
        {
            _failed = true;
            Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _idle?.Dispose();
        _read?.Dispose();
        _write?.Dispose();
        _transfer?.Dispose();
        _queue?.Dispose();
        _idle = _read = _write = _transfer = _queue = null;
    }
}
