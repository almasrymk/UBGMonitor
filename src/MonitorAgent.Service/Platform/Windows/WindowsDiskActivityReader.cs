using System.Diagnostics;
using System.Runtime.Versioning;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Windows;

/// <summary>The PhysicalDisk "_Total" performance counters.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskActivityReader : IDiskActivityReader, IDisposable
{
    private const string Category = "PhysicalDisk";
    private const string Instance = "_Total";

    private readonly ILogger<WindowsDiskActivityReader> _logger;
    private PerformanceCounter? _idle, _read, _write, _transfer, _queue;

    public WindowsDiskActivityReader(ILogger<WindowsDiskActivityReader> logger)
    {
        _logger = logger;
    }

    public bool Start()
    {
        try
        {
            _idle = new PerformanceCounter(Category, "% Idle Time", Instance, readOnly: true);
            _read = new PerformanceCounter(Category, "Disk Read Bytes/sec", Instance, readOnly: true);
            _write = new PerformanceCounter(Category, "Disk Write Bytes/sec", Instance, readOnly: true);
            _transfer = new PerformanceCounter(Category, "Avg. Disk sec/Transfer", Instance, readOnly: true);
            _queue = new PerformanceCounter(Category, "Current Disk Queue Length", Instance, readOnly: true);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Disk activity counters are unavailable");
            Dispose();
            return false;
        }
    }

    public DiskActivityDto Read() => new()
    {
        ActiveTimePercent = Math.Clamp(100d - _idle!.NextValue(), 0, 100),
        ReadBytesPerSecond = Math.Max(0, _read!.NextValue()),
        WriteBytesPerSecond = Math.Max(0, _write!.NextValue()),
        ResponseMs = Math.Max(0, _transfer!.NextValue() * 1000d),
        QueueLength = Math.Max(0, _queue!.NextValue())
    };

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
