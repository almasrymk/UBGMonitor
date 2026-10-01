using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using static MonitorAgent.Service.Platform.InventoryText;

namespace MonitorAgent.Service.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsInventoryCollector : IInventoryCollector
{
    /// <summary>
    /// Classes that cannot change while Windows is running are read once. They include the slowest queries
    /// (Win32_Processor, SoftwareLicensingService), which otherwise ran on every refresh of the hardware screen.
    /// </summary>
    private readonly Dictionary<string, List<Dictionary<string, string?>>> _fixed = new();

    public HardwareInventory Collect()
    {
        var system = First("Win32_ComputerSystem");
        var product = Fixed("Win32_ComputerSystemProduct").FirstOrDefault() ?? [];
        var processor = Fixed("Win32_Processor").FirstOrDefault() ?? [];
        var bios = Fixed("Win32_BIOS").FirstOrDefault() ?? [];
        var board = Fixed("Win32_BaseBoard").FirstOrDefault() ?? [];
        var enclosure = Fixed("Win32_SystemEnclosure").FirstOrDefault() ?? [];
        var os = First("Win32_OperatingSystem");
        var timezone = First("Win32_TimeZone");
        var gpu = First("Win32_VideoController");
        var memoryArray = Fixed("Win32_PhysicalMemoryArray").FirstOrDefault() ?? [];
        var license = Fixed("SoftwareLicensingService").FirstOrDefault() ?? [];
        var memories = Fixed("Win32_PhysicalMemory");
        var disks = All("Win32_DiskDrive");
        var adapters = All("Win32_NetworkAdapter");
        var monitors = All("Win32_DesktopMonitor");
        var sounds = All("Win32_SoundDevice");
        var disk = disks.FirstOrDefault() ?? [];

        var ramTotalBytes = memories.Sum(m => ParseLong(Val(m, "Capacity")));
        if (ramTotalBytes <= 0)
        {
            ramTotalBytes = ParseLong(Val(system, "TotalPhysicalMemory"));
        }

        var ramSpeed = memories.Select(m => ParseInt(Val(m, "Speed"))).FirstOrDefault(v => v > 0);
        var ramType = memories.Select(m => ParseInt(Val(m, "SMBIOSMemoryType"))).FirstOrDefault(v => v > 0);
        var ramKind = RamTypeName(ramType);
        var ramGb = Math.Round(Measure.BytesToGb(ramTotalBytes), 0);
        var gpuVramGb = Measure.Round(Measure.BytesToGb(ParseLong(Val(gpu, "AdapterRAM"))));
        var gpuName = Text(Val(gpu, "Name"));
        var boardMfr = Text(Val(board, "Manufacturer"), emptyAsUnknown: false);
        var boardProduct = Text(Val(board, "Product"));
        var motherboard = string.IsNullOrWhiteSpace(boardMfr) ? boardProduct : $"{boardMfr} {boardProduct}".Trim();
        var lastBoot = ParseWmiDate(Val(os, "LastBootUpTime"));
        var installDate = ParseWmiDate(Val(os, "InstallDate"));
        var biosDate = ParseWmiDate(Val(bios, "ReleaseDate"));
        var adapter = PickPhysicalAdapter(adapters);
        var width = ParseInt(Val(gpu, "CurrentHorizontalResolution"));
        var height = ParseInt(Val(gpu, "CurrentVerticalResolution"));

        return new HardwareInventory
        {
            OsFamily = "Windows",
            Manufacturer = Text(Val(system, "Manufacturer")),
            Model = Text(Val(system, "Model")),
            SystemType = Text(Val(system, "SystemType")),
            CpuName = Text(Val(processor, "Name")).Trim(),
            CpuCores = Positive(Val(processor, "NumberOfCores"), Environment.ProcessorCount.ToString()),
            CpuThreads = Positive(Val(processor, "NumberOfLogicalProcessors"), Environment.ProcessorCount.ToString()),
            CpuSpeed = FormatGhz(ParseDouble(Val(processor, "MaxClockSpeed"))),
            RamTotal = ramGb > 0 ? $"{ramGb:0} GB" : "-",
            RamSpeed = ramSpeed > 0 ? $"{ramSpeed} MHz" : "-",
            GpuName = gpuName,
            GpuVram = gpuVramGb > 0 ? $"{gpuVramGb:0} GB" : "-",
            Motherboard = motherboard,
            BiosVersion = Text(Val(bios, "SMBIOSBIOSVersion")),
            BiosDate = biosDate?.ToString("yyyy-MM-dd") ?? "-",
            SerialNumber = Text(Val(bios, "SerialNumber")),
            Uuid = Text(Val(product, "UUID")),
            ChassisType = ChassisName(Val(enclosure, "ChassisTypes")),
            RamSlotsUsed = memories.Count.ToString(),
            RamSlotsTotal = Positive(Val(memoryArray, "MemoryDevices"), memories.Count.ToString()),
            RamType = ramKind,
            RamManufacturer = JoinDistinct(memories, "Manufacturer"),
            DiskModel = JoinDistinct(disks, "Model"),
            DiskSize = FormatDiskSizes(disks),
            DiskType = JoinDistinct(disks, "MediaType"),
            DiskInterface = JoinDistinct(disks, "InterfaceType"),
            MonitorName = JoinDistinct(monitors, "Name"),
            MonitorRes = width > 0 && height > 0 ? $"{width} x {height}" : "-",
            SoundCard = JoinDistinct(sounds, "Name"),
            NetworkAdapter = Text(Val(adapter, "Name")),
            MacAddress = FormatMac(Val(adapter, "MACAddress")),
            BiosSerial = Text(Val(bios, "SerialNumber")),
            BaseboardSerial = Text(Val(board, "SerialNumber")),
            ProcessorId = Text(Val(processor, "ProcessorId")),
            ProcessorFamily = Text(Val(processor, "Family")),
            ProcessorSocket = Text(Val(processor, "SocketDesignation")),
            L2Cache = FormatCacheKb(ParseInt(Val(processor, "L2CacheSize"))),
            L3Cache = FormatCacheKb(ParseInt(Val(processor, "L3CacheSize"))),
            RamPartNumber = JoinDistinct(memories, "PartNumber"),
            RamSerial = JoinDistinct(memories, "SerialNumber"),
            DiskSerial = JoinDistinct(disks, "SerialNumber"),
            DiskFirmware = JoinDistinct(disks, "FirmwareRevision"),
            DiskPartitions = Count("Win32_DiskPartition").ToString(),
            GpuDriverVer = Text(Val(gpu, "DriverVersion")),
            GpuDriverDate = ParseWmiDate(Val(gpu, "DriverDate"))?.ToString("yyyy-MM-dd") ?? "-",
            LastBoot = lastBoot,
            InstallDate = installDate,
            Timezone = Text(Val(timezone, "Caption"), fallback: TimeZoneInfo.Local.DisplayName),
            Locale = FormatLocale(Val(os, "Locale")),
            OsEdition = Text(Val(os, "Caption"), fallback: Environment.OSVersion.ToString()),
            OsVersion = Text(Val(os, "Version"), fallback: Environment.OSVersion.Version.ToString()),
            OsBuild = Text(Val(os, "BuildNumber")),
            ProductKey = Text(Val(license, "OA3xOriginalProductKey")),
            Architecture = Text(Val(os, "OSArchitecture"), fallback: Environment.Is64BitOperatingSystem ? "x64" : "x86"),
            GpuCombined = gpuVramGb > 0 ? $"{gpuName} ({gpuVramGb:0}GB)" : gpuName,
            RamCombined = ramSpeed > 0 ? $"{ramGb:0} GB {ramKind} {ramSpeed}MHz" : $"{ramGb:0} GB {ramKind}",
            DiskCombined = FormatPrimaryDisk(disk),
            CoresThreads = $"{Positive(Val(processor, "NumberOfCores"), Environment.ProcessorCount.ToString())} / {Positive(Val(processor, "NumberOfLogicalProcessors"), Environment.ProcessorCount.ToString())}",
            GpuVramGb = gpuVramGb
        };
    }

    private static Dictionary<string, string?> First(string className)
        => All(className).FirstOrDefault() ?? [];

    private List<Dictionary<string, string?>> Fixed(string className)
    {
        lock (_fixed)
        {
            if (!_fixed.TryGetValue(className, out var rows) || rows.Count == 0)
            {
                rows = All(className);
                _fixed[className] = rows;
            }

            return rows;
        }
    }

    private static List<Dictionary<string, string?>> All(string className, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, string?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, $"SELECT * FROM {className}");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in obj.Properties)
                    {
                        row[property.Name] = ConvertValue(property.Value);
                    }

                    rows.Add(row);
                }
            }
        }
        catch
        {
            // WMI class may be missing on this machine.
        }

        return rows;
    }

    private static int Count(string className)
    {
        var count = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT __PATH FROM {className}");
            using var results = searcher.Get();
            count += results.Count;
        }
        catch
        {
            // Ignore.
        }

        return count;
    }

    private static string? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is ushort[] chassis)
        {
            return string.Join(",", chassis);
        }

        if (value is Array array)
        {
            return string.Join(",", array.Cast<object?>().Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        return value.ToString();
    }

    private static string? Val(Dictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value : null;

    private static Dictionary<string, string?> PickPhysicalAdapter(List<Dictionary<string, string?>> adapters)
    {
        return adapters.FirstOrDefault(a =>
                   !string.IsNullOrWhiteSpace(Val(a, "MACAddress"))
                   && (string.Equals(Val(a, "PhysicalAdapter"), "True", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(Val(a, "NetEnabled"), "True", StringComparison.OrdinalIgnoreCase)))
               ?? adapters.FirstOrDefault(a => !string.IsNullOrWhiteSpace(Val(a, "MACAddress")))
               ?? [];
    }

    private static string JoinDistinct(List<Dictionary<string, string?>> rows, string key)
    {
        var values = rows
            .Select(row => Normalize(Val(row, key)))
            .Where(v => v != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "-" : string.Join(" | ", values);
    }

    private static string FormatDiskSizes(List<Dictionary<string, string?>> disks)
    {
        var sizes = disks
            .Select(d => Measure.Round(Measure.BytesToGb(ParseLong(Val(d, "Size")))))
            .Where(v => v > 0)
            .Select(v => $"{v:0} GB")
            .ToArray();
        return sizes.Length == 0 ? "-" : string.Join(" | ", sizes);
    }

    private static string FormatPrimaryDisk(Dictionary<string, string?> disk)
    {
        var model = Text(Val(disk, "Model"));
        var sizeGb = Measure.Round(Measure.BytesToGb(ParseLong(Val(disk, "Size"))));
        var iface = Normalize(Val(disk, "InterfaceType"));
        if (model == "-" && sizeGb <= 0)
        {
            return "-";
        }

        return iface == "-"
            ? $"{model} {sizeGb:0}GB"
            : $"{model} {sizeGb:0}GB {iface}";
    }

    private static string FormatLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "-";
        }

        if (int.TryParse(locale, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
        {
            try
            {
                return CultureInfo.GetCultureInfo(lcid).Name;
            }
            catch
            {
                // Keep the WMI locale code.
            }
        }

        return locale;
    }

    private static string ChassisName(string? raw)
    {
        var code = raw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseInt)
            .FirstOrDefault(v => v > 0) ?? 0;
        return code switch
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
            30 => "Tablet",
            31 => "Convertible",
            32 => "Detachable",
            > 0 => $"Type {code}",
            _ => "-"
        };
    }

    private static string RamTypeName(int type) => type switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        0 => "-",
        _ => $"Type {type}"
    };

    private static string Positive(string? value, string fallback)
        => ParseInt(value) > 0 ? ParseInt(value).ToString() : fallback;

    private static DateTime? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 14)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch
        {
            return null;
        }
    }

    private static int ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static long ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value)
        => double.TryParse(value, out var parsed) ? parsed : 0;
}
