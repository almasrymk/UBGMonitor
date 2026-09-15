using System.Globalization;
using System.Management;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.SystemInfo;

public interface IHardwareService
{
    Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default);

    Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default);

    Task<HardwareResponseDto> GetStaticLevelsAsync(CancellationToken cancellationToken = default);

    Task<HardwareLevelDto?> GetLevelAsync(int level, CancellationToken cancellationToken = default);
}

public sealed class HardwareService : IHardwareService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly ILogger<HardwareService> _logger;
    private readonly object _lock = new();
    private Inventory? _cached;
    private DateTime _cachedAtUtc;

    public HardwareService(ILogger<HardwareService> logger)
    {
        _logger = logger;
    }

    public Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => MapHardware(GetInventory()), cancellationToken);

    public Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => MapOs(GetInventory()), cancellationToken);

    public Task<HardwareResponseDto> GetStaticLevelsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var inventory = GetInventory();
            return new HardwareResponseDto
            {
                Timestamp = DateTime.UtcNow,
                Levels =
                [
                    MapLevel1(inventory),
                    MapLevel2(inventory),
                    MapLevel4(inventory)
                ]
            };
        }, cancellationToken);

    public Task<HardwareLevelDto?> GetLevelAsync(int level, CancellationToken cancellationToken = default)
        => Task.Run<HardwareLevelDto?>(() =>
        {
            var inventory = GetInventory();
            return level switch
            {
                1 => MapLevel1(inventory),
                2 => MapLevel2(inventory),
                4 => MapLevel4(inventory),
                _ => null
            };
        }, cancellationToken);

    private Inventory GetInventory()
    {
        lock (_lock)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return _cached;
            }

            try
            {
                _cached = Collect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect hardware inventory");
                _cached ??= new Inventory();
            }

            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static Inventory Collect()
    {
        var system = First("Win32_ComputerSystem");
        var product = First("Win32_ComputerSystemProduct");
        var processor = First("Win32_Processor");
        var bios = First("Win32_BIOS");
        var board = First("Win32_BaseBoard");
        var enclosure = First("Win32_SystemEnclosure");
        var os = First("Win32_OperatingSystem");
        var timezone = First("Win32_TimeZone");
        var gpu = First("Win32_VideoController");
        var memoryArray = First("Win32_PhysicalMemoryArray");
        var license = First("SoftwareLicensingService");
        var memories = All("Win32_PhysicalMemory");
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
        var ramGb = Math.Round(BytesToGb(ramTotalBytes), 0);
        var gpuVramGb = Round(BytesToGb(ParseLong(Val(gpu, "AdapterRAM"))));
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

        return new Inventory
        {
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
            L2Cache = FormatCacheKb(Val(processor, "L2CacheSize")),
            L3Cache = FormatCacheKb(Val(processor, "L3CacheSize")),
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
            WindowsEdition = Text(Val(os, "Caption"), fallback: Environment.OSVersion.ToString()),
            WindowsVersion = Text(Val(os, "Version"), fallback: Environment.OSVersion.Version.ToString()),
            WindowsBuild = Text(Val(os, "BuildNumber")),
            ProductKey = Text(Val(license, "OA3xOriginalProductKey")),
            Architecture = Text(Val(os, "OSArchitecture"), fallback: Environment.Is64BitOperatingSystem ? "x64" : "x86"),
            GpuCombined = gpuVramGb > 0 ? $"{gpuName} ({gpuVramGb:0}GB)" : gpuName,
            RamCombined = ramSpeed > 0 ? $"{ramGb:0} GB {ramKind} {ramSpeed}MHz" : $"{ramGb:0} GB {ramKind}",
            DiskCombined = FormatPrimaryDisk(disk),
            CoresThreads = $"{Positive(Val(processor, "NumberOfCores"), Environment.ProcessorCount.ToString())} / {Positive(Val(processor, "NumberOfLogicalProcessors"), Environment.ProcessorCount.ToString())}",
            GpuVramGb = gpuVramGb
        };
    }

    private static HardwareInfo MapHardware(Inventory inv) => new()
    {
        Manufacturer = inv.Manufacturer,
        Model = inv.Model,
        SerialNumber = inv.SerialNumber,
        Cpu = inv.CpuName,
        CoresThreads = inv.CoresThreads,
        CpuSpeed = inv.CpuSpeed,
        Ram = inv.RamCombined,
        Gpu = inv.GpuCombined,
        Disk = inv.DiskCombined,
        Motherboard = inv.Motherboard,
        BiosVersion = inv.BiosVersion,
        BiosDate = inv.BiosDate,
        GpuModel = inv.GpuName,
        GpuVramGB = inv.GpuVramGb
    };

    private static OsInfo MapOs(Inventory inv)
    {
        var lastBoot = inv.LastBoot;
        return new OsInfo
        {
            Name = inv.WindowsEdition,
            Version = inv.WindowsVersion,
            Build = inv.WindowsBuild,
            Architecture = inv.Architecture,
            InstallDate = inv.InstallDate,
            LastBoot = lastBoot,
            Uptime = lastBoot.HasValue ? DateTime.Now - lastBoot.Value : TimeSpan.Zero,
            Timezone = inv.Timezone,
            Locale = inv.Locale,
            SystemType = inv.SystemType
        };
    }

    private static HardwareLevelDto MapLevel1(Inventory inv) => Level(1, "Basic Hardware",
    [
        Item("Manufacturer", inv.Manufacturer),
        Item("Model", inv.Model),
        Item("System Type", inv.SystemType),
        Item("CPU Name", inv.CpuName),
        Item("CPU Cores", inv.CpuCores),
        Item("CPU Threads", inv.CpuThreads),
        Item("CPU Speed", inv.CpuSpeed),
        Item("RAM Total", inv.RamTotal),
        Item("RAM Speed", inv.RamSpeed),
        Item("GPU Name", inv.GpuName),
        Item("GPU VRAM", inv.GpuVram),
        Item("Motherboard", inv.Motherboard),
        Item("BIOS Version", inv.BiosVersion),
        Item("BIOS Date", inv.BiosDate)
    ]);

    private static HardwareLevelDto MapLevel2(Inventory inv) => Level(2, "Extended Hardware",
    [
        Item("Serial Number", inv.SerialNumber),
        Item("UUID", inv.Uuid),
        Item("Chassis Type", inv.ChassisType),
        Item("RAM Slots Used", inv.RamSlotsUsed),
        Item("RAM Slots Total", inv.RamSlotsTotal),
        Item("RAM Type", inv.RamType),
        Item("RAM Manufacturer", inv.RamManufacturer),
        Item("Disk Model", inv.DiskModel),
        Item("Disk Size", inv.DiskSize),
        Item("Disk Type", inv.DiskType),
        Item("Disk Interface", inv.DiskInterface),
        Item("Monitor Name", inv.MonitorName),
        Item("Monitor Res", inv.MonitorRes),
        Item("Sound Card", inv.SoundCard),
        Item("Network Adapter", inv.NetworkAdapter),
        Item("MAC Address", inv.MacAddress)
    ]);

    private static HardwareLevelDto MapLevel4(Inventory inv)
    {
        var lastBoot = inv.LastBoot;
        var uptime = lastBoot.HasValue ? DateTime.Now - lastBoot.Value : TimeSpan.Zero;
        return Level(4, "Detailed Info",
        [
            Item("BIOS Serial", inv.BiosSerial),
            Item("Baseboard Serial", inv.BaseboardSerial),
            Item("Processor ID", inv.ProcessorId),
            Item("Processor Family", inv.ProcessorFamily),
            Item("Processor Socket", inv.ProcessorSocket),
            Item("L2 Cache Size", inv.L2Cache),
            Item("L3 Cache Size", inv.L3Cache),
            Item("RAM Part Number", inv.RamPartNumber),
            Item("RAM Serial", inv.RamSerial),
            Item("Disk Serial", inv.DiskSerial),
            Item("Disk Firmware", inv.DiskFirmware),
            Item("Disk Partitions", inv.DiskPartitions),
            Item("GPU Driver Ver", inv.GpuDriverVer),
            Item("GPU Driver Date", inv.GpuDriverDate),
            Item("System Boot", lastBoot?.ToString("yyyy-MM-dd HH:mm") ?? "-"),
            Item("System Uptime", FormatUptime(uptime)),
            Item("Timezone", inv.Timezone),
            Item("Locale", inv.Locale),
            Item("Windows Edition", inv.WindowsEdition),
            Item("Windows Version", inv.WindowsVersion),
            Item("Windows Build", inv.WindowsBuild),
            Item("Install Date", inv.InstallDate?.ToString("yyyy-MM-dd") ?? "-"),
            Item("Last Boot", lastBoot?.ToString("yyyy-MM-dd HH:mm") ?? "-"),
            Item("Product Key", inv.ProductKey)
        ]);
    }

    private static HardwareLevelDto Level(int level, string title, List<HardwareItemDto> items)
        => new()
        {
            Level = level,
            Title = title,
            Items = items,
            ItemCount = items.Count
        };

    private static HardwareItemDto Item(string name, string? value)
    {
        var text = Normalize(value);
        return new HardwareItemDto
        {
            Name = name,
            Value = text,
            Status = text == "-" ? "Red" : "Green"
        };
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value is "Unknown" or "unknown" ? "-" : value.Trim();

    private static Dictionary<string, string?> First(string className)
        => All(className).FirstOrDefault() ?? [];

    private static List<Dictionary<string, string?>> All(string className, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, string?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, $"SELECT * FROM {className}");
            foreach (ManagementObject obj in searcher.Get())
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
            count += searcher.Get().Count;
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
            .Select(d => Round(BytesToGb(ParseLong(Val(d, "Size")))))
            .Where(v => v > 0)
            .Select(v => $"{v:0} GB")
            .ToArray();
        return sizes.Length == 0 ? "-" : string.Join(" | ", sizes);
    }

    private static string FormatPrimaryDisk(Dictionary<string, string?> disk)
    {
        var model = Text(Val(disk, "Model"));
        var sizeGb = Round(BytesToGb(ParseLong(Val(disk, "Size"))));
        var iface = Normalize(Val(disk, "InterfaceType"));
        if (model == "-" && sizeGb <= 0)
        {
            return "-";
        }

        return iface == "-"
            ? $"{model} {sizeGb:0}GB"
            : $"{model} {sizeGb:0}GB {iface}";
    }

    private static string FormatGhz(double mhz)
        => mhz > 0 ? $"{mhz / 1000d:0.0} GHz" : "-";

    private static string FormatCacheKb(string? kbRaw)
    {
        var kb = ParseInt(kbRaw);
        if (kb <= 0)
        {
            return "-";
        }

        return kb >= 1024 ? $"{kb / 1024d:0} MB" : $"{kb} KB";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime <= TimeSpan.Zero)
        {
            return "-";
        }

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }

        return $"{uptime.Hours}h {uptime.Minutes}m";
    }

    private static string FormatMac(string? raw)
    {
        var value = raw?.Replace("-", string.Empty).Replace(":", string.Empty).Trim() ?? string.Empty;
        if (value.Length != 12)
        {
            return Normalize(raw);
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => value.Substring(i * 2, 2)));
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

    private static string Text(string? value, string fallback = "Unknown", bool emptyAsUnknown = true)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return emptyAsUnknown ? fallback : string.Empty;
    }

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

    private static double BytesToGb(long bytes) => bytes / 1024d / 1024d / 1024d;

    private static double Round(double value) => Math.Round(value, 2);

    private static int ParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static long ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : 0;

    private static double ParseDouble(string? value)
        => double.TryParse(value, out var parsed) ? parsed : 0;

    private sealed class Inventory
    {
        public string Manufacturer { get; init; } = "Unknown";
        public string Model { get; init; } = "Unknown";
        public string SystemType { get; init; } = "Unknown";
        public string CpuName { get; init; } = "Unknown";
        public string CpuCores { get; init; } = "Unknown";
        public string CpuThreads { get; init; } = "Unknown";
        public string CpuSpeed { get; init; } = "Unknown";
        public string RamTotal { get; init; } = "Unknown";
        public string RamSpeed { get; init; } = "Unknown";
        public string GpuName { get; init; } = "Unknown";
        public string GpuVram { get; init; } = "Unknown";
        public string Motherboard { get; init; } = "Unknown";
        public string BiosVersion { get; init; } = "Unknown";
        public string BiosDate { get; init; } = "Unknown";
        public string SerialNumber { get; init; } = "Unknown";
        public string Uuid { get; init; } = "Unknown";
        public string ChassisType { get; init; } = "Unknown";
        public string RamSlotsUsed { get; init; } = "Unknown";
        public string RamSlotsTotal { get; init; } = "Unknown";
        public string RamType { get; init; } = "Unknown";
        public string RamManufacturer { get; init; } = "Unknown";
        public string DiskModel { get; init; } = "Unknown";
        public string DiskSize { get; init; } = "Unknown";
        public string DiskType { get; init; } = "Unknown";
        public string DiskInterface { get; init; } = "Unknown";
        public string MonitorName { get; init; } = "Unknown";
        public string MonitorRes { get; init; } = "Unknown";
        public string SoundCard { get; init; } = "Unknown";
        public string NetworkAdapter { get; init; } = "Unknown";
        public string MacAddress { get; init; } = "Unknown";
        public string BiosSerial { get; init; } = "Unknown";
        public string BaseboardSerial { get; init; } = "Unknown";
        public string ProcessorId { get; init; } = "Unknown";
        public string ProcessorFamily { get; init; } = "Unknown";
        public string ProcessorSocket { get; init; } = "Unknown";
        public string L2Cache { get; init; } = "Unknown";
        public string L3Cache { get; init; } = "Unknown";
        public string RamPartNumber { get; init; } = "Unknown";
        public string RamSerial { get; init; } = "Unknown";
        public string DiskSerial { get; init; } = "Unknown";
        public string DiskFirmware { get; init; } = "Unknown";
        public string DiskPartitions { get; init; } = "Unknown";
        public string GpuDriverVer { get; init; } = "Unknown";
        public string GpuDriverDate { get; init; } = "Unknown";
        public DateTime? LastBoot { get; init; }
        public DateTime? InstallDate { get; init; }
        public string Timezone { get; init; } = "Unknown";
        public string Locale { get; init; } = "Unknown";
        public string WindowsEdition { get; init; } = "Unknown";
        public string WindowsVersion { get; init; } = "Unknown";
        public string WindowsBuild { get; init; } = "Unknown";
        public string ProductKey { get; init; } = "Unknown";
        public string Architecture { get; init; } = "Unknown";
        public string GpuCombined { get; init; } = "Unknown";
        public string RamCombined { get; init; } = "Unknown";
        public string DiskCombined { get; init; } = "Unknown";
        public string CoresThreads { get; init; } = "Unknown";
        public double GpuVramGb { get; init; }
    }
}
