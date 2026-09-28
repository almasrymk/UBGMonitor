using System.Diagnostics;

namespace ClientAgent.UI.Services;

public readonly record struct NetworkActivity(double ReceivedKbps, double SentKbps);

/// <summary>Samples "Network Interface" counters for the active adapter, like Task Manager's send/receive graph.</summary>
public sealed class NetworkActivitySampler : IDisposable
{
    private const string Category = "Network Interface";

    private readonly List<(PerformanceCounter Received, PerformanceCounter Sent)> _counters = [];
    private string? _adapter;
    private bool _primed;
    private bool _failed;

    public NetworkActivity? Sample(string? adapterDescription)
    {
        if (_failed)
        {
            return null;
        }

        try
        {
            if (!_primed || !string.Equals(_adapter, adapterDescription, StringComparison.OrdinalIgnoreCase))
            {
                Open(adapterDescription);
                return null;
            }

            double received = 0;
            double sent = 0;
            foreach (var (receivedCounter, sentCounter) in _counters)
            {
                received += receivedCounter.NextValue();
                sent += sentCounter.NextValue();
            }

            return new NetworkActivity(Math.Max(0, received * 8d / 1000d), Math.Max(0, sent * 8d / 1000d));
        }
        catch (InvalidOperationException)
        {
            _primed = false;
            return null;
        }
        catch
        {
            _failed = true;
            Dispose();
            return null;
        }
    }

    private void Open(string? adapterDescription)
    {
        Dispose();
        _adapter = adapterDescription;
        var instances = new PerformanceCounterCategory(Category).GetInstanceNames();
        var wanted = Normalize(adapterDescription);
        var selected = instances.Where(name => wanted.Length > 0 && name.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
        {
            selected = instances.Where(name => !name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                                               && !name.Contains("isatap", StringComparison.OrdinalIgnoreCase)
                                               && !name.Contains("Teredo", StringComparison.OrdinalIgnoreCase)
                                               && !name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
                                               && !name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var instance in selected)
        {
            var received = new PerformanceCounter(Category, "Bytes Received/sec", instance, readOnly: true);
            var sent = new PerformanceCounter(Category, "Bytes Sent/sec", instance, readOnly: true);
            received.NextValue();
            sent.NextValue();
            _counters.Add((received, sent));
        }

        _primed = true;
    }

    // Performance counter instance names escape characters that are reserved in counter paths.
    private static string Normalize(string? description) =>
        (description ?? string.Empty).Trim()
            .Replace('(', '[')
            .Replace(')', ']')
            .Replace('#', '_')
            .Replace('/', '_')
            .Replace('\\', '_');

    public void Dispose()
    {
        foreach (var (received, sent) in _counters)
        {
            received.Dispose();
            sent.Dispose();
        }

        _counters.Clear();
    }
}
