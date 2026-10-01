using System.Diagnostics;
using System.Runtime.Versioning;

namespace MonitorAgent.Service.Platform.Windows;

/// <summary>The "Network Interface" performance counters: they also see traffic the adapter byte totals miss.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNetworkCounters : IDisposable
{
    private readonly Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> _counters = new(StringComparer.OrdinalIgnoreCase);

    public WindowsNetworkCounters()
    {
        var category = new PerformanceCounterCategory("Network Interface");
        foreach (var instance in category.GetInstanceNames())
        {
            if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                || instance.Contains("_Total", StringComparison.OrdinalIgnoreCase)
                || instance.Contains("isatap", StringComparison.OrdinalIgnoreCase)
                || instance.Contains("Teredo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rx = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, readOnly: true);
            var tx = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, readOnly: true);
            rx.NextValue();
            tx.NextValue();
            _counters[instance] = (rx, tx);
        }
    }

    /// <summary>Received and sent megabits per second over all adapters.</summary>
    public (double Rx, double Tx) ReadMbps()
    {
        double rx = 0;
        double tx = 0;
        foreach (var pair in _counters.Values)
        {
            try
            {
                rx += pair.Rx.NextValue();
                tx += pair.Tx.NextValue();
            }
            catch
            {
                // Adapter may have disappeared.
            }
        }

        return (rx * 8d / 1_000_000d, tx * 8d / 1_000_000d);
    }

    public void Dispose()
    {
        foreach (var pair in _counters.Values)
        {
            pair.Rx.Dispose();
            pair.Tx.Dispose();
        }

        _counters.Clear();
    }
}
