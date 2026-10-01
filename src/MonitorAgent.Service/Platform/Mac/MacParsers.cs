using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonitorAgent.Service.Platform.Mac;

public sealed record VmStat(long PageSize, long Free, long Inactive, long Speculative, long FileBacked, long Purgeable);

public sealed record BlockStats(long BytesRead, long BytesWritten, long Operations, long TotalTimeNs);

public sealed record MacBattery(int? Percent, string Status, bool OnAc);

public sealed record MacDisk(string Name, string Model, string Bsd, long SizeBytes, string Interface, string Smart, string Serial, string Firmware, bool Ssd);

public sealed record MacWifi(string Interface, string Ssid, int? SignalPercent, string Channel, string Band, string PhyMode, double? RateMbps);

/// <summary>Reads the output of the macOS tools. Pure functions, so they can be tested anywhere.</summary>
public static partial class MacParsers
{
    public static VmStat ParseVmStat(string text)
    {
        var pageSize = PageSizeRegex().Match(text) is { Success: true } m ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 4096;
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            var index = line.IndexOf(':');
            if (index > 0 && long.TryParse(line[(index + 1)..].Trim().TrimEnd('.'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values[line[..index].Trim()] = value;
            }
        }

        return new VmStat(
            pageSize,
            values.GetValueOrDefault("Pages free"),
            values.GetValueOrDefault("Pages inactive"),
            values.GetValueOrDefault("Pages speculative"),
            values.GetValueOrDefault("File-backed pages"),
            values.GetValueOrDefault("Pages purgeable"));
    }

    /// <summary>sysctl vm.swapusage: "total = 2048.00M  used = 1024.50M  free = 1023.50M  (encrypted)", in bytes.</summary>
    public static (long Total, long Free) ParseSwapUsage(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        long Size(string name)
        {
            var match = Regex.Match(text, $@"{name}\s*=\s*([\d.]+)([KMGT])", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return 0;
            }

            var number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var factor = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "K" => 1024d,
                "M" => 1024d * 1024,
                "G" => 1024d * 1024 * 1024,
                _ => 1024d * 1024 * 1024 * 1024
            };
            return (long)(number * factor);
        }

        return (Size("total"), Size("free"));
    }

    /// <summary>
    /// The "Statistics" of each disk in "ioreg -c IOBlockStorageDriver -r -w 0", summed:
    /// {"Bytes (Read)"=1,"Operations (Read)"=2,"Total Time (Read)"=3,...}.
    /// </summary>
    public static BlockStats ParseIoregStatistics(string text)
    {
        long bytesRead = 0, bytesWritten = 0, operations = 0, time = 0;
        foreach (Match statistics in StatisticsRegex().Matches(text))
        {
            var body = statistics.Groups[1].Value;
            bytesRead += Field(body, "Bytes (Read)");
            bytesWritten += Field(body, "Bytes (Write)");
            operations += Field(body, "Operations (Read)") + Field(body, "Operations (Write)");
            time += Field(body, "Total Time (Read)") + Field(body, "Total Time (Write)");
        }

        return new BlockStats(bytesRead, bytesWritten, operations, time);

        static long Field(string body, string name)
        {
            var match = Regex.Match(body, $"\"{Regex.Escape(name)}\"=(\\d+)");
            return match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }

    /// <summary>"nettop -P -L 1 -x -J bytes_in,bytes_out": bytes in plus out of each process ("name.pid,in,out,").</summary>
    public static Dictionary<int, ulong> ParseNettop(string text)
    {
        var result = new Dictionary<int, ulong>();
        foreach (var line in Lines(text))
        {
            var fields = line.Split(',');
            for (var i = 0; i < fields.Length - 2; i++)
            {
                var match = ProcessRegex().Match(fields[i]);
                if (match.Success
                    && ulong.TryParse(fields[i + 1], out var bytesIn)
                    && ulong.TryParse(fields[i + 2], out var bytesOut))
                {
                    var pid = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    result[pid] = result.GetValueOrDefault(pid) + bytesIn + bytesOut;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>"pmset -g batt": "Now drawing from 'AC Power'" and "-InternalBattery-0 (id=1)	85%; charging; 1:20 remaining".</summary>
    public static MacBattery? ParsePmsetBattery(string text)
    {
        var onAc = text.Contains("'AC Power'", StringComparison.Ordinal);
        var match = BatteryRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var state = match.Groups[2].Value.Trim().ToLowerInvariant();
        var status = state switch
        {
            "charging" => "Charging",
            "discharging" => "Discharging",
            "charged" => "Fully Charged",
            "finishing charge" => "Charging",
            "ac attached" => "Plugged in",
            _ => onAc ? "Plugged in" : "Discharging"
        };
        return new MacBattery(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), status, onAc);
    }

    /// <summary>Device names of the Wi-Fi ports in "networksetup -listallhardwareports".</summary>
    public static HashSet<string> ParseWifiPorts(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var lines = Lines(text).ToList();
        for (var i = 0; i < lines.Count - 1; i++)
        {
            if (lines[i].StartsWith("Hardware Port:", StringComparison.Ordinal)
                && (lines[i].Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || lines[i].Contains("AirPort", StringComparison.OrdinalIgnoreCase))
                && lines[i + 1].StartsWith("Device:", StringComparison.Ordinal))
            {
                result.Add(lines[i + 1]["Device:".Length..].Trim());
            }
        }

        return result;
    }

    /// <summary>The disks in "system_profiler SPNVMeDataType SPSerialATADataType SPUSBDataType -json" (controllers hold the disks in _items).</summary>
    public static List<MacDisk> ParseStorage(string json)
    {
        var result = new List<MacDisk>();
        using var document = Parse(json);
        if (document is null)
        {
            return result;
        }

        foreach (var (type, iface) in new[] { ("SPNVMeDataType", "NVMe"), ("SPSerialATADataType", "SATA") })
        {
            if (!document.RootElement.TryGetProperty(type, out var controllers) || controllers.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var controller in controllers.EnumerateArray())
            {
                foreach (var disk in Items(controller))
                {
                    result.Add(new MacDisk(
                        Str(disk, "_name") ?? "Disk",
                        Str(disk, "device_model") ?? Str(disk, "_name") ?? "Disk",
                        Str(disk, "bsd_name") ?? string.Empty,
                        Long(disk, "size_in_bytes"),
                        iface,
                        Str(disk, "smart_status") ?? "Unknown",
                        Str(disk, "device_serial") ?? "-",
                        Str(disk, "device_revision") ?? "-",
                        !string.Equals(Str(disk, "spsata_medium_type"), "Rotational", StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        return result;
    }

    /// <summary>The connected network of each interface in "system_profiler SPAirPortDataType -json".</summary>
    public static List<MacWifi> ParseAirport(string json)
    {
        var result = new List<MacWifi>();
        using var document = Parse(json);
        if (document is null || !document.RootElement.TryGetProperty("SPAirPortDataType", out var root) || root.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in root.EnumerateArray())
        {
            if (!entry.TryGetProperty("spairport_airport_interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in interfaces.EnumerateArray())
            {
                if (!item.TryGetProperty("spairport_current_network_information", out var network))
                {
                    continue;
                }

                var channel = Str(network, "spairport_network_channel") ?? "-";
                var rssi = Regex.Match(Str(network, "spairport_signal_noise") ?? string.Empty, @"(-?\d+)\s*dBm");
                result.Add(new MacWifi(
                    Str(item, "_name") ?? "en0",
                    Str(network, "_name") ?? "-",
                    rssi.Success ? Math.Clamp(2 * (int.Parse(rssi.Groups[1].Value, CultureInfo.InvariantCulture) + 100), 0, 100) : null,
                    channel.Split(' ')[0],
                    Regex.Match(channel, @"(\d(?:\.\d)?)\s*GHz") is { Success: true } band ? $"{band.Groups[1].Value} GHz" : "-",
                    Str(network, "spairport_network_phymode") ?? "-",
                    network.TryGetProperty("spairport_network_rate", out var rate) && rate.TryGetDouble(out var mbps) ? mbps : null));
            }
        }

        return result;
    }

    public static JsonDocument? Parse(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IEnumerable<JsonElement> Items(JsonElement element)
        => element.TryGetProperty("_items", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    public static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static long Long(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static IEnumerable<string> Lines(string text)
        => text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);

    [GeneratedRegex(@"page size of (\d+) bytes")]
    private static partial Regex PageSizeRegex();

    [GeneratedRegex(@"""Statistics""\s*=\s*\{([^}]*)\}")]
    private static partial Regex StatisticsRegex();

    [GeneratedRegex(@"^.+\.(\d+)$")]
    private static partial Regex ProcessRegex();

    [GeneratedRegex(@"(\d+)%;\s*([^;]+);")]
    private static partial Regex BatteryRegex();
}
