using System.Globalization;

namespace MonitorAgent.Service.Platform.Linux;

public sealed record CpuTimes(long Busy, long Total);

public sealed record CpuInfoSummary(string Model, int PhysicalCores, int LogicalCores, double AverageMhz);

public sealed record DiskStat(string Name, long SectorsRead, long SectorsWritten, long Ios, long IoMs, long InFlight, long BusyMs);

public sealed record MemoryModule(string Size, string Type, int SpeedMhz, string Manufacturer, string PartNumber, string Serial);

public sealed record MemoryInventory(IReadOnlyList<MemoryModule> Modules, int Slots);

public sealed record NmcliWifi(string Device, string Ssid, int? Signal, string Channel, string FrequencyMhz, double? RateMbps);

/// <summary>Reads the text formats of /proc, /sys and the Linux tools. Pure functions, so they can be tested anywhere.</summary>
public static class LinuxParsers
{
    /// <summary>The "cpu" lines of /proc/stat: "cpu" is the total, "cpu0", "cpu1"... the cores.</summary>
    public static Dictionary<string, CpuTimes> ParseProcStat(string text)
    {
        var result = new Dictionary<string, CpuTimes>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = parts.Skip(1).Select(ParseLong).ToArray();
            if (values.Length < 4)
            {
                continue;
            }

            // user nice system idle iowait irq softirq steal (guest time is already counted in user).
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            var total = values.Take(Math.Min(8, values.Length)).Sum();
            result[parts[0]] = new CpuTimes(total - idle, total);
        }

        return result;
    }

    public static double UsagePercent(CpuTimes before, CpuTimes after)
    {
        var total = after.Total - before.Total;
        return total <= 0 ? 0 : Math.Clamp((after.Busy - before.Busy) * 100d / total, 0, 100);
    }

    /// <summary>The boot time ("btime" seconds since 1970) from /proc/stat.</summary>
    public static DateTime? ParseBootTime(string procStat)
    {
        var line = Lines(procStat).FirstOrDefault(l => l.StartsWith("btime ", StringComparison.Ordinal));
        return line is not null && long.TryParse(line[6..].Trim(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            : null;
    }

    public static CpuInfoSummary ParseCpuInfo(string text)
    {
        string? model = null;
        var logical = 0;
        var cores = new HashSet<string>(StringComparer.Ordinal);
        var physicalId = "0";
        var mhz = new List<double>();
        foreach (var line in Lines(text))
        {
            var (key, value) = KeyValue(line, ':');
            switch (key)
            {
                case "processor":
                    logical++;
                    break;
                case "model name" or "Model" or "Hardware" when model is null:
                    model = value;
                    break;
                case "physical id":
                    physicalId = value;
                    break;
                case "core id":
                    cores.Add($"{physicalId}:{value}");
                    break;
                case "cpu MHz" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed):
                    mhz.Add(speed);
                    break;
            }
        }

        logical = Math.Max(logical, 1);
        return new CpuInfoSummary(
            model ?? "Unknown",
            cores.Count > 0 ? cores.Count : logical,
            logical,
            mhz.Count > 0 ? Math.Round(mhz.Average(), 0) : 0);
    }

    /// <summary>/proc/meminfo values in bytes ("MemTotal: 16314164 kB").</summary>
    public static Dictionary<string, long> ParseMemInfo(string text)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            var (key, value) = KeyValue(line, ':');
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (key.Length > 0 && parts.Length > 0 && long.TryParse(parts[0], out var number))
            {
                result[key] = parts.Length > 1 && parts[1] == "kB" ? number * 1024 : number;
            }
        }

        return result;
    }

    /// <summary>The whole-disk rows of /proc/diskstats (partitions, loop, ram and device-mapper rows are left out).</summary>
    public static List<DiskStat> ParseDiskStats(string text)
    {
        var result = new List<DiskStat>();
        foreach (var line in Lines(text))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 14 || !IsPhysicalDiskName(parts[2]))
            {
                continue;
            }

            var reads = ParseLong(parts[3]);
            var writes = ParseLong(parts[7]);
            result.Add(new DiskStat(
                parts[2],
                ParseLong(parts[5]),
                ParseLong(parts[9]),
                reads + writes,
                ParseLong(parts[6]) + ParseLong(parts[10]),
                ParseLong(parts[11]),
                ParseLong(parts[12])));
        }

        return result;
    }

    /// <summary>sda, vda, xvda, hda, nvme0n1, mmcblk0: a disk, not a partition, loop, RAM or virtual mapper device.</summary>
    public static bool IsPhysicalDiskName(string name)
    {
        if (name.StartsWith("nvme", StringComparison.Ordinal))
        {
            return !name.Contains('p', StringComparison.Ordinal) || name.LastIndexOf('p') < name.LastIndexOf('n');
        }

        if (name.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            return !name.Contains('p', StringComparison.Ordinal) && !name.Contains("boot", StringComparison.Ordinal) && !name.Contains("rpmb", StringComparison.Ordinal);
        }

        return (name.StartsWith("sd", StringComparison.Ordinal) || name.StartsWith("vd", StringComparison.Ordinal)
                || name.StartsWith("xvd", StringComparison.Ordinal) || name.StartsWith("hd", StringComparison.Ordinal))
               && !char.IsDigit(name[^1]);
    }

    /// <summary>KEY=value lines (/etc/os-release, uevent files); quotes are removed.</summary>
    public static Dictionary<string, string> ParseKeyValueFile(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(text))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var (key, value) = KeyValue(line, '=');
            if (key.Length > 0)
            {
                result[key] = value.Trim('"', '\'');
            }
        }

        return result;
    }

    /// <summary>The memory devices of "dmidecode -t 16,17": one per slot, empty slots included with Size "No Module Installed".</summary>
    public static MemoryInventory ParseDmidecodeMemory(string text)
    {
        var modules = new List<MemoryModule>();
        var slots = 0;
        var arraySlots = 0;
        Dictionary<string, string>? device = null;

        void Flush()
        {
            if (device is null)
            {
                return;
            }

            slots++;
            var size = device.GetValueOrDefault("Size", string.Empty);
            if (size.Length > 0 && !size.StartsWith("No Module", StringComparison.OrdinalIgnoreCase) && size != "Unknown")
            {
                var speed = new[] { "Configured Memory Speed", "Configured Clock Speed", "Speed" }
                    .Select(key => ParseLeadingInt(device.GetValueOrDefault(key)))
                    .FirstOrDefault(value => value > 0);
                modules.Add(new MemoryModule(
                    size,
                    device.GetValueOrDefault("Type", "-"),
                    speed,
                    Clean(device.GetValueOrDefault("Manufacturer")),
                    Clean(device.GetValueOrDefault("Part Number")),
                    Clean(device.GetValueOrDefault("Serial Number"))));
            }

            device = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Memory Device", StringComparison.Ordinal))
            {
                Flush();
                device = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            if (line.StartsWith("Handle ", StringComparison.Ordinal) || line.Length == 0)
            {
                Flush();
                continue;
            }

            var (key, value) = KeyValue(line, ':');
            if (key == "Number Of Devices")
            {
                arraySlots += ParseLeadingInt(value);
            }
            else if (device is not null && key.Length > 0)
            {
                device.TryAdd(key, value);
            }
        }

        Flush();
        return new MemoryInventory(modules, arraySlots > 0 ? arraySlots : slots);
    }

    /// <summary>"nmcli -t -f ACTIVE,DEVICE,SSID,SIGNAL,CHAN,FREQ,RATE device wifi list": the networks this computer is connected to.</summary>
    public static List<NmcliWifi> ParseNmcliWifi(string text)
    {
        var result = new List<NmcliWifi>();
        foreach (var line in Lines(text))
        {
            var fields = SplitTerse(line);
            if (fields.Count < 7 || !fields[0].Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new NmcliWifi(
                fields[1],
                fields[2],
                int.TryParse(fields[3], out var signal) ? signal : null,
                fields[4],
                fields[5].Split(' ')[0],
                ParseLeadingInt(fields[6]) is var rate and > 0 ? rate : null));
        }

        return result;
    }

    /// <summary>The GPUs in "lspci -mm" output: the device name of each VGA / 3D / display controller.</summary>
    public static List<string> ParseLspciGpus(string text)
    {
        var result = new List<string>();
        foreach (var line in Lines(text))
        {
            var fields = QuotedFields(line);
            if (fields.Count >= 3 && (fields[0].Contains("VGA", StringComparison.OrdinalIgnoreCase)
                                      || fields[0].Contains("3D controller", StringComparison.OrdinalIgnoreCase)
                                      || fields[0].Contains("Display controller", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add($"{ShortVendor(fields[1])} {fields[2]}".Trim());
            }
        }

        return result;
    }

    /// <summary>The device and mount point of each line in /proc/mounts (escaped spaces decoded).</summary>
    public static List<(string Device, string MountPoint, string FileSystem)> ParseMounts(string text)
    {
        var result = new List<(string, string, string)>();
        foreach (var line in Lines(text))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                result.Add((parts[0], parts[1].Replace("\\040", " "), parts[2]));
            }
        }

        return result;
    }

    public static string ChassisName(int code) => code switch
    {
        1 => "Other",
        3 => "Desktop",
        4 => "Low Profile Desktop",
        6 => "Mini Tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Hand Held",
        13 => "All in One",
        14 => "Sub Notebook",
        17 => "Main Server Chassis",
        23 => "Rack Mount Chassis",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        35 => "Mini PC",
        > 0 => $"Type {code}",
        _ => "-"
    };

    public static int ParseLeadingInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var digits = new string(text.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string ShortVendor(string vendor) => vendor
        .Replace(" Corporation", string.Empty)
        .Replace(", Inc.", string.Empty)
        .Replace(" Inc.", string.Empty)
        .Replace("Advanced Micro Devices, Inc. [AMD/ATI]", "AMD")
        .Replace("[AMD/ATI]", string.Empty)
        .Trim();

    private static string Clean(string? value)
        => string.IsNullOrWhiteSpace(value) || value is "Unknown" or "Not Specified" or "NO DIMM" || value.StartsWith("0000", StringComparison.Ordinal) ? "-" : value.Trim();

    private static List<string> SplitTerse(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length)
            {
                current.Append(line[++i]);
            }
            else if (line[i] == ':')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(line[i]);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static List<string> QuotedFields(string line)
    {
        var fields = new List<string>();
        var start = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != '"')
            {
                continue;
            }

            if (start < 0)
            {
                start = i + 1;
            }
            else
            {
                fields.Add(line[start..i]);
                start = -1;
            }
        }

        return fields;
    }

    private static (string Key, string Value) KeyValue(string line, char separator)
    {
        var index = line.IndexOf(separator);
        return index <= 0 ? (string.Empty, string.Empty) : (line[..index].Trim(), line[(index + 1)..].Trim());
    }

    private static IEnumerable<string> Lines(string text)
        => text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0);

    private static long ParseLong(string text) => long.TryParse(text, out var value) ? value : 0;
}
