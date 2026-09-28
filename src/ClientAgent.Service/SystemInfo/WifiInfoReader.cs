using System.Diagnostics;
using System.Globalization;

namespace ClientAgent.Service.SystemInfo;

internal sealed record WifiDetails(
    string InterfaceName,
    string Description,
    string Ssid,
    int? SignalPercent,
    string Band,
    string Channel,
    string RadioType,
    double? ReceiveMbps,
    double? TransmitMbps);

/// <summary>Reads Wi-Fi details from <c>netsh wlan show interfaces</c>, cached to avoid spawning a process per request.</summary>
internal static class WifiInfoReader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private static readonly object Gate = new();
    private static IReadOnlyList<WifiDetails> _cached = [];
    private static DateTime _cachedAtUtc;
    private static bool _refreshing;

    public static WifiDetails? Find(string interfaceName, string description)
    {
        RefreshIfStale();
        IReadOnlyList<WifiDetails> snapshot;
        lock (Gate)
        {
            snapshot = _cached;
        }

        return snapshot.FirstOrDefault(w => w.InterfaceName.Equals(interfaceName, StringComparison.OrdinalIgnoreCase))
               ?? snapshot.FirstOrDefault(w => w.Description.Equals(description, StringComparison.OrdinalIgnoreCase))
               ?? (snapshot.Count == 1 ? snapshot[0] : null);
    }

    private static void RefreshIfStale()
    {
        bool firstLoad;
        lock (Gate)
        {
            if (_refreshing || DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return;
            }

            _refreshing = true;
            firstLoad = _cachedAtUtc == default;
        }

        if (firstLoad)
        {
            Refresh();
        }
        else
        {
            _ = Task.Run(Refresh);
        }
    }

    private static void Refresh()
    {
        IReadOnlyList<WifiDetails> result = [];
        try
        {
            result = Parse(RunNetsh());
        }
        catch
        {
            // WLAN service may be stopped or absent on wired-only machines.
        }

        lock (Gate)
        {
            _cached = result;
            _cachedAtUtc = DateTime.UtcNow;
            _refreshing = false;
        }
    }

    private static string RunNetsh()
    {
        using var process = Process.Start(new ProcessStartInfo("netsh", "wlan show interfaces")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(3000))
        {
            process.Kill();
        }

        return output;
    }

    private static List<WifiDetails> Parse(string output)
    {
        var list = new List<WifiDetails>();
        Dictionary<string, string>? current = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var separator = rawLine.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = rawLine[..separator].Trim();
            var value = rawLine[(separator + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                AddIfConnected(list, current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (current is not null)
            {
                current.TryAdd(key, value);
            }
        }

        AddIfConnected(list, current);
        return list;
    }

    private static void AddIfConnected(List<WifiDetails> list, Dictionary<string, string>? values)
    {
        if (values is null || !Get(values, "State").Equals("connected", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        list.Add(new WifiDetails(
            Get(values, "Name"),
            Get(values, "Description"),
            Get(values, "SSID"),
            ParseNumber(Get(values, "Signal").TrimEnd('%', ' ')) is double signal ? (int)signal : null,
            Get(values, "Band"),
            Get(values, "Channel"),
            Get(values, "Radio type"),
            ParseNumber(Get(values, "Receive rate (Mbps)")),
            ParseNumber(Get(values, "Transmit rate (Mbps)"))));
    }

    private static string Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : string.Empty;

    private static double? ParseNumber(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
