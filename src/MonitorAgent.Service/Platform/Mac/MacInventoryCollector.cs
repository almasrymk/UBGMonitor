using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using static MonitorAgent.Service.Platform.InventoryText;

namespace MonitorAgent.Service.Platform.Mac;

/// <summary>Hardware and system details from system_profiler (the data behind "About This Mac") and sysctl.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacInventoryCollector : IInventoryCollector
{
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);
    private (string Json, DateTime AtUtc)? _profile;

    public HardwareInventory Collect()
    {
        var json = Profile();
        using var document = MacParsers.Parse(json);
        var root = document?.RootElement;
        var hardware = First(root, "SPHardwareDataType");
        var software = First(root, "SPSoftwareDataType");
        var display = First(root, "SPDisplaysDataType");
        var memory = First(root, "SPMemoryDataType");
        var screens = display is { } d && d.TryGetProperty("spdisplays_ndrvs", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [];
        var modules = memory is { } m ? MacParsers.Items(m).ToList() : [];
        var audio = Array(root, "SPAudioDataType").SelectMany(MacParsers.Items).Select(a => MacParsers.Str(a, "_name")).ToList();
        var storage = MacParsers.ParseStorage(Command.Run("system_profiler", "SPNVMeDataType SPSerialATADataType -json", 20_000)?.Output ?? string.Empty);

        var cpu = MacNative.SysctlString("machdep.cpu.brand_string") ?? Get(hardware, "chip_type") ?? Get(hardware, "cpu_type") ?? "Unknown";
        var physical = MacNative.SysctlLong("hw.physicalcpu") ?? Environment.ProcessorCount;
        var logical = MacNative.SysctlLong("hw.logicalcpu") ?? Environment.ProcessorCount;
        var ramGb = Math.Round(Measure.BytesToGb(MacNative.SysctlLong("hw.memsize") ?? 0), 0);
        var ramType = Get(memory, "dimm_type") ?? modules.Select(x => MacParsers.Str(x, "dimm_type")).FirstOrDefault(t => t is not null) ?? "-";
        var ramSpeed = LeadingInt(modules.Select(x => MacParsers.Str(x, "dimm_speed")).FirstOrDefault(s => s is not null));
        var gpu = Get(display, "sppci_model") ?? Get(display, "_name") ?? "-";
        var vram = Get(display, "spdisplays_vram") ?? Get(display, "spdisplays_vram_shared") ?? "-";
        var osVersion = Get(software, "os_version") ?? RuntimeInformation.OSDescription;
        var primary = storage.FirstOrDefault();
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var speedMhz = (MacNative.SysctlLong("hw.cpufrequency_max") ?? 0) / 1_000_000d;
        var adapter = Command.Run("ifconfig", "en0", 2000)?.Output ?? string.Empty;
        var mac = System.Text.RegularExpressions.Regex.Match(adapter, @"ether\s+([0-9a-f:]{17})");

        return new HardwareInventory
        {
            OsFamily = "macOS",
            Manufacturer = "Apple",
            Model = Text(Get(hardware, "machine_name")) + (Get(hardware, "machine_model") is { } id ? $" ({id})" : string.Empty),
            SystemType = arch,
            CpuName = cpu,
            CpuCores = physical.ToString(CultureInfo.InvariantCulture),
            CpuThreads = logical.ToString(CultureInfo.InvariantCulture),
            CpuSpeed = speedMhz > 0 ? FormatGhz(speedMhz) : Text(Get(hardware, "current_processor_speed"), "-"),
            RamTotal = ramGb > 0 ? $"{ramGb:0} GB" : "-",
            RamSpeed = ramSpeed > 0 ? $"{ramSpeed} MHz" : "-",
            GpuName = gpu,
            GpuVram = vram,
            Motherboard = "Apple",
            BiosVersion = Text(Get(hardware, "boot_rom_version") ?? Get(hardware, "os_loader_version"), "-"),
            BiosDate = "-",
            SerialNumber = Text(Get(hardware, "serial_number"), "-"),
            Uuid = Text(Get(hardware, "platform_UUID"), "-"),
            ChassisType = (Get(hardware, "machine_name") ?? string.Empty).Contains("Book", StringComparison.OrdinalIgnoreCase) ? "Laptop" : "Desktop",
            RamSlotsUsed = modules.Count > 0 ? modules.Count(x => MacParsers.Str(x, "dimm_size") is { } s && !s.Contains("Empty", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture) : "-",
            RamSlotsTotal = modules.Count > 0 ? modules.Count.ToString(CultureInfo.InvariantCulture) : "-",
            RamType = ramType,
            RamManufacturer = Get(memory, "dimm_manufacturer") ?? Join(modules.Select(x => MacParsers.Str(x, "dimm_manufacturer"))),
            DiskModel = Join(storage.Select(s => s.Model)),
            DiskSize = storage.Count == 0 ? "-" : string.Join(" | ", storage.Select(s => $"{Measure.Round(Measure.BytesToGb(s.SizeBytes)):0} GB")),
            DiskType = Join(storage.Select(s => s.Ssd ? "SSD" : "HDD")),
            DiskInterface = Join(storage.Select(s => s.Interface)),
            MonitorName = Join(screens.Select(s => MacParsers.Str(s, "_name"))),
            MonitorRes = Join(screens.Select(s => MacParsers.Str(s, "_spdisplays_resolution") ?? MacParsers.Str(s, "spdisplays_resolution"))),
            SoundCard = Join(audio),
            NetworkAdapter = mac.Success ? "en0" : "-",
            MacAddress = mac.Success ? FormatMac(mac.Groups[1].Value) : "-",
            BiosSerial = Text(Get(hardware, "serial_number"), "-"),
            BaseboardSerial = "-",
            ProcessorId = "-",
            ProcessorFamily = Text(Get(hardware, "chip_type") ?? Get(hardware, "cpu_type"), "-"),
            ProcessorSocket = "-",
            L2Cache = FormatCacheKb((int)((MacNative.SysctlLong("hw.l2cachesize") ?? 0) / 1024)),
            L3Cache = FormatCacheKb((int)((MacNative.SysctlLong("hw.l3cachesize") ?? 0) / 1024)),
            RamPartNumber = Join(modules.Select(x => MacParsers.Str(x, "dimm_part_number"))),
            RamSerial = Join(modules.Select(x => MacParsers.Str(x, "dimm_serial_number"))),
            DiskSerial = Join(storage.Select(s => s.Serial)),
            DiskFirmware = Join(storage.Select(s => s.Firmware)),
            DiskPartitions = "-",
            GpuDriverVer = "-",
            GpuDriverDate = "-",
            LastBoot = MacNative.BootTime(),
            InstallDate = InstallDate(),
            Timezone = TimeZoneInfo.Local.Id,
            Locale = CultureInfo.CurrentCulture.Name is { Length: > 0 } locale ? locale : "-",
            OsEdition = osVersion,
            OsVersion = MacNative.SysctlString("kern.osproductversion") ?? Text(osVersion),
            OsBuild = MacNative.SysctlString("kern.osversion") ?? "-",
            Architecture = arch,
            GpuCombined = vram is "-" ? gpu : $"{gpu} ({vram})",
            RamCombined = ramSpeed > 0 ? $"{ramGb:0} GB {ramType} {ramSpeed}MHz" : $"{ramGb:0} GB {ramType}",
            DiskCombined = primary is null ? "-" : $"{primary.Model} {Measure.Round(Measure.BytesToGb(primary.SizeBytes)):0}GB {primary.Interface}",
            CoresThreads = $"{physical} / {logical}",
            GpuVramGb = 0
        };
    }

    private string Profile()
    {
        if (_profile is { } cached && DateTime.UtcNow - cached.AtUtc < ProfileTtl)
        {
            return cached.Json;
        }

        var json = Command.Run("system_profiler", "SPHardwareDataType SPSoftwareDataType SPDisplaysDataType SPMemoryDataType SPAudioDataType -json", 30_000)?.Output ?? string.Empty;
        _profile = (json, DateTime.UtcNow);
        return json;
    }

    private static IEnumerable<JsonElement> Array(JsonElement? root, string name)
        => root is { } r && r.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];

    private static JsonElement? First(JsonElement? root, string name)
    {
        foreach (var element in Array(root, name))
        {
            return element.Clone();
        }

        return null;
    }

    private static string? Get(JsonElement? element, string name) => element is { } e ? MacParsers.Str(e, name) : null;

    private static int LeadingInt(string? text)
        => int.TryParse(new string((text ?? string.Empty).TakeWhile(char.IsDigit).ToArray()), out var value) ? value : 0;

    /// <summary>The setup assistant writes this file when macOS is installed.</summary>
    private static DateTime? InstallDate()
    {
        const string path = "/var/db/.AppleSetupDone";
        try
        {
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Join(IEnumerable<string?> values)
    {
        var distinct = values.Select(Normalize).Where(v => v != "-").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.Length == 0 ? "-" : string.Join(" | ", distinct);
    }
}
