using System.ComponentModel;
using System.Diagnostics;
using MonitorAgent.Service.Platform;

namespace MonitorAgent.Service.SystemInfo;

public sealed record ProcessUsage(int Pid, string Name, string? Path, double CpuPercent, double RamMB, double NetworkKBps, double DiskKBps);

/// <summary>
/// What every process uses, as the change since the previous reading: processor time, disk and network bytes. The first
/// reading (or one after a long gap) waits half a second for a second sample; readings close together share one.
/// </summary>
public sealed class ProcessUsageSampler
{
    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NetworkBudget = TimeSpan.FromMilliseconds(800);

    private readonly ISystemProbe _probe;
    private readonly IHostInventory _inventory;
    private readonly ILogger<ProcessUsageSampler> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<int, (string Name, string? Path)> _paths = [];
    private Reading? _last;
    private IReadOnlyList<ProcessUsage> _result = [];
    private long _resultAt;

    public ProcessUsageSampler(ISystemProbe probe, IHostInventory inventory, ILogger<ProcessUsageSampler> logger)
    {
        _probe = probe;
        _inventory = inventory;
        _logger = logger;
    }

    public IReadOnlyList<ProcessUsage> Read()
    {
        lock (_lock)
        {
            if (_resultAt != 0 && Stopwatch.GetElapsedTime(_resultAt) < Reuse)
            {
                return _result;
            }

            var previous = _last;
            if (previous is null || Stopwatch.GetElapsedTime(previous.Timestamp) > MaxGap)
            {
                previous = Capture();
                Thread.Sleep(500);
            }

            var current = Capture();
            var seconds = (current.Timestamp - previous.Timestamp) / (double)Stopwatch.Frequency;
            var cores = Math.Max(1, Environment.ProcessorCount);
            var usage = new List<ProcessUsage>(current.Processes.Count);
            foreach (var (pid, now) in current.Processes)
            {
                Counters? before = previous.Processes.TryGetValue(pid, out var found) && found.Name == now.Name ? found : null;
                usage.Add(new ProcessUsage(
                    pid,
                    now.Name,
                    PathOf(pid, now.Name),
                    before is { } b && seconds > 0 && now.CpuMs >= b.CpuMs ? Math.Clamp((now.CpuMs - b.CpuMs) / (seconds * 1000 * cores) * 100, 0, 100) : 0,
                    now.RamMB,
                    Rate(now.Network, before?.Network, seconds),
                    Rate(now.Disk, before?.Disk, seconds)));
            }

            foreach (var gone in _paths.Keys.Where(pid => !current.Processes.ContainsKey(pid)).ToList())
            {
                _paths.Remove(gone);
            }

            _last = current;
            _result = usage;
            _resultAt = Stopwatch.GetTimestamp();
            return usage;
        }
    }

    private static double Rate(ulong now, ulong? before, double seconds)
        => before is { } previous && now >= previous && seconds > 0 ? (now - previous) / seconds / 1024d : 0;

    private string? PathOf(int pid, string name)
    {
        if (_paths.TryGetValue(pid, out var known) && known.Name == name)
        {
            return known.Path;
        }

        var path = _inventory.ProcessPath(pid);
        _paths[pid] = (name, path);
        return path;
    }

    private Reading Capture()
    {
        var disk = Safe(_probe.ReadProcessDiskBytes);
        var network = Safe(() => _probe.ReadProcessNetworkBytes(NetworkBudget));
        var processes = new Dictionary<int, Counters>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                processes[process.Id] = new Counters(
                    process.ProcessName,
                    CpuMilliseconds(process),
                    process.WorkingSet64 / 1024d / 1024d,
                    disk.GetValueOrDefault(process.Id),
                    network.GetValueOrDefault(process.Id));
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // The process ended while it was being read.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new Reading(Stopwatch.GetTimestamp(), processes);
    }

    /// <summary>Protected processes do not give their processor time, even to the service; they count as idle.</summary>
    private static double CpuMilliseconds(Process process)
    {
        try
        {
            return process.TotalProcessorTime.TotalMilliseconds;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private Dictionary<int, ulong> Safe(Func<Dictionary<int, ulong>> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read per-process counters");
            return [];
        }
    }

    private sealed record Reading(long Timestamp, Dictionary<int, Counters> Processes);

    private readonly record struct Counters(string Name, double CpuMs, double RamMB, ulong Disk, ulong Network);
}
