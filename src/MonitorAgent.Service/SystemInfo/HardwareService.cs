using MonitorAgent.Service.Config;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.SystemInfo;

public interface IHardwareService
{
    Task<HardwareInfo> GetHardwareAsync(CancellationToken cancellationToken = default);

    Task<OsInfo> GetOsAsync(CancellationToken cancellationToken = default);

    Task<HardwareResponseDto> GetStaticLevelsAsync(CancellationToken cancellationToken = default);

    Task<HardwareLevelDto?> GetLevelAsync(int level, CancellationToken cancellationToken = default);
}

public sealed class HardwareService : IHardwareService
{
    private readonly ILogger<HardwareService> _logger;
    private readonly ILocalConfigCache _config;
    private readonly IInventoryCollector _collector;
    private readonly object _lock = new();
    private HardwareInventory? _cached;
    private DateTime _cachedAtUtc;

    public HardwareService(ILocalConfigCache config, IInventoryCollector collector, ILogger<HardwareService> logger)
    {
        _config = config;
        _collector = collector;
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

    private HardwareInventory GetInventory()
    {
        lock (_lock)
        {
            var ttl = TimeSpan.FromSeconds(_config.GetGeneral().Seconds(_config.GetGeneral().HardwareOsIntervalSeconds, 30));
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < ttl)
            {
                return _cached;
            }

            try
            {
                _cached = _collector.Collect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect hardware inventory");
                _cached ??= new HardwareInventory();
            }

            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static HardwareInfo MapHardware(HardwareInventory inv) => new()
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

    private static OsInfo MapOs(HardwareInventory inv)
    {
        var lastBoot = inv.LastBoot;
        return new OsInfo
        {
            Name = inv.OsEdition,
            Version = inv.OsVersion,
            Build = inv.OsBuild,
            Architecture = inv.Architecture,
            InstallDate = inv.InstallDate,
            LastBoot = lastBoot,
            Uptime = lastBoot.HasValue ? DateTime.Now - lastBoot.Value : TimeSpan.Zero,
            Timezone = inv.Timezone,
            Locale = inv.Locale,
            SystemType = inv.SystemType
        };
    }

    private static HardwareLevelDto MapLevel1(HardwareInventory inv) => Level(1, "Basic Hardware",
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

    private static HardwareLevelDto MapLevel2(HardwareInventory inv) => Level(2, "Extended Hardware",
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

    private static HardwareLevelDto MapLevel4(HardwareInventory inv)
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
            Item($"{inv.OsFamily} Edition", inv.OsEdition),
            Item($"{inv.OsFamily} Version", inv.OsVersion),
            Item($"{inv.OsFamily} Build", inv.OsBuild),
            Item("Install Date", inv.InstallDate?.ToString("yyyy-MM-dd") ?? "-"),
            Item("Last Boot", lastBoot?.ToString("yyyy-MM-dd HH:mm") ?? "-"),
            .. inv.OsFamily == "Windows" ? [Item("Product Key", inv.ProductKey)] : Array.Empty<HardwareItemDto>()
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

    private static string Normalize(string? value) => InventoryText.Normalize(value);

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

}
