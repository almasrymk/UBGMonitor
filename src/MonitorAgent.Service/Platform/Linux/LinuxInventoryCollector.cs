using System.Globalization;
using System.Runtime.InteropServices;
using static MonitorAgent.Service.Platform.InventoryText;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>
/// Hardware from /sys/class/dmi (the same SMBIOS tables Windows reads), the system from /etc/os-release, and the
/// details only root can read (memory modules, serial numbers) from dmidecode, which the service runs as root.
/// </summary>
public sealed class LinuxInventoryCollector : IInventoryCollector
{
    private const string DmiFolder = "/sys/class/dmi/id";

    public HardwareInventory Collect()
    {
        var os = LinuxParsers.ParseKeyValueFile(LinuxFiles.Read("/etc/os-release"));
        var cpu = LinuxParsers.ParseCpuInfo(LinuxFiles.Read("/proc/cpuinfo"));
        var maxKhz = LinuxFiles.ReadLong("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
        var memInfo = LinuxParsers.ParseMemInfo(LinuxFiles.Read("/proc/meminfo"));
        var memory = LinuxParsers.ParseDmidecodeMemory(Command.Run("dmidecode", "-t 16,17", 5000)?.Output ?? string.Empty);
        var gpus = LinuxParsers.ParseLspciGpus(Command.Run("lspci", "-mm", 5000)?.Output ?? string.Empty);
        var disks = LinuxDisks.Read();
        var adapter = PhysicalAdapter();
        var boot = LinuxParsers.ParseBootTime(LinuxFiles.Read("/proc/stat"));

        var ramGb = Math.Round(Measure.BytesToGb(memInfo.GetValueOrDefault("MemTotal")), 0);
        var ramSpeed = memory.Modules.Select(m => m.SpeedMhz).FirstOrDefault(s => s > 0);
        var ramType = memory.Modules.Select(m => m.Type).FirstOrDefault(t => t is not ("-" or "Unknown" or "Other")) ?? "-";
        var boardVendor = Dmi("board_vendor");
        var boardName = Dmi("board_name");
        var cores = cpu.PhysicalCores.ToString(CultureInfo.InvariantCulture);
        var threads = cpu.LogicalCores.ToString(CultureInfo.InvariantCulture);
        var gpu = gpus.Count == 0 ? "-" : string.Join(" | ", gpus);
        var primary = disks.FirstOrDefault();
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

        return new HardwareInventory
        {
            OsFamily = "Linux",
            Manufacturer = Text(Dmi("sys_vendor")),
            Model = Text(Dmi("product_name")),
            SystemType = arch,
            CpuName = cpu.Model,
            CpuCores = cores,
            CpuThreads = threads,
            CpuSpeed = FormatGhz(maxKhz is > 0 ? maxKhz.Value / 1000d : cpu.AverageMhz),
            RamTotal = ramGb > 0 ? $"{ramGb:0} GB" : "-",
            RamSpeed = ramSpeed > 0 ? $"{ramSpeed} MHz" : "-",
            GpuName = gpu,
            GpuVram = "-",
            Motherboard = string.IsNullOrWhiteSpace(boardVendor) ? Text(boardName) : $"{boardVendor} {boardName}".Trim(),
            BiosVersion = Text(Dmi("bios_version")),
            BiosDate = FormatBiosDate(Dmi("bios_date")),
            SerialNumber = Text(Dmi("product_serial")),
            Uuid = Text(Dmi("product_uuid")),
            ChassisType = LinuxParsers.ChassisName(LinuxParsers.ParseLeadingInt(Dmi("chassis_type"))),
            RamSlotsUsed = memory.Modules.Count > 0 ? memory.Modules.Count.ToString(CultureInfo.InvariantCulture) : "-",
            RamSlotsTotal = memory.Slots > 0 ? memory.Slots.ToString(CultureInfo.InvariantCulture) : "-",
            RamType = ramType,
            RamManufacturer = Join(memory.Modules.Select(m => m.Manufacturer)),
            DiskModel = Join(disks.Select(d => d.Model)),
            DiskSize = disks.Count == 0 ? "-" : string.Join(" | ", disks.Select(d => $"{d.SizeGB:0} GB")),
            DiskType = Join(disks.Select(d => d.Type)),
            DiskInterface = Join(disks.Select(d => d.Interface)),
            MonitorName = "-",
            MonitorRes = "-",
            SoundCard = Join(SoundCards()),
            NetworkAdapter = adapter is null ? "-" : Path.GetFileName(adapter),
            MacAddress = adapter is null ? "-" : FormatMac(LinuxFiles.ReadLine($"{adapter}/address")),
            BiosSerial = Text(Dmi("product_serial")),
            BaseboardSerial = Text(Dmi("board_serial")),
            ProcessorId = "-",
            ProcessorFamily = Text(CpuField("cpu family")),
            ProcessorSocket = "-",
            L2Cache = FormatCacheKb(CacheKb(2)),
            L3Cache = FormatCacheKb(CacheKb(3)),
            RamPartNumber = Join(memory.Modules.Select(m => m.PartNumber)),
            RamSerial = Join(memory.Modules.Select(m => m.Serial)),
            DiskSerial = Join(LinuxDisks.DiskNames().Select(n => LinuxFiles.ReadLine($"/sys/block/{n}/device/serial"))),
            DiskFirmware = Join(LinuxDisks.DiskNames().Select(n => LinuxFiles.ReadLine($"/sys/block/{n}/device/firmware_rev") ?? LinuxFiles.ReadLine($"/sys/block/{n}/device/rev"))),
            DiskPartitions = LinuxDisks.DiskNames()
                .Sum(n => LinuxFiles.Directories($"/sys/block/{n}").Count(d => Path.GetFileName(d).StartsWith(n, StringComparison.Ordinal)))
                .ToString(CultureInfo.InvariantCulture),
            GpuDriverVer = "-",
            GpuDriverDate = "-",
            LastBoot = boot,
            InstallDate = InstallDate(),
            Timezone = TimeZoneInfo.Local.Id,
            Locale = CultureInfo.CurrentCulture.Name is { Length: > 0 } locale ? locale : "-",
            OsEdition = Text(os.GetValueOrDefault("PRETTY_NAME") ?? os.GetValueOrDefault("NAME"), fallback: RuntimeInformation.OSDescription),
            OsVersion = Text(os.GetValueOrDefault("VERSION_ID") ?? os.GetValueOrDefault("VERSION")),
            OsBuild = Text(LinuxFiles.ReadLine("/proc/sys/kernel/osrelease")),
            Architecture = arch,
            GpuCombined = gpu,
            RamCombined = ramSpeed > 0 ? $"{ramGb:0} GB {ramType} {ramSpeed}MHz" : $"{ramGb:0} GB {ramType}".Trim(),
            DiskCombined = primary is null ? "-" : $"{primary.Model} {primary.SizeGB:0}GB {primary.Interface}",
            CoresThreads = $"{cores} / {threads}",
            GpuVramGb = 0
        };
    }

    private static string? Dmi(string name) => LinuxFiles.ReadLine($"{DmiFolder}/{name}");

    private static string? CpuField(string key)
        => LinuxFiles.Read("/proc/cpuinfo").Split('\n')
            .Select(line => line.Split(':', 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0].Trim() == key)?[1].Trim();

    private static int CacheKb(int level)
    {
        foreach (var index in LinuxFiles.Directories("/sys/devices/system/cpu/cpu0/cache"))
        {
            if (LinuxFiles.ReadLine($"{index}/level") == level.ToString(CultureInfo.InvariantCulture)
                && LinuxFiles.ReadLine($"{index}/type") is "Unified" or "Data")
            {
                var size = LinuxFiles.ReadLine($"{index}/size") ?? string.Empty;
                var number = LinuxParsers.ParseLeadingInt(size);
                return size.EndsWith('M') ? number * 1024 : number;
            }
        }

        return 0;
    }

    /// <summary>The first adapter backed by real hardware (virtual ones such as docker0 or veth have no device link).</summary>
    private static string? PhysicalAdapter()
        => LinuxFiles.Directories("/sys/class/net")
            .Where(folder => Directory.Exists($"{folder}/device"))
            .OrderBy(folder => LinuxFiles.ReadLine($"{folder}/operstate") == "up" ? 0 : 1)
            .FirstOrDefault();

    private static IEnumerable<string> SoundCards()
        => LinuxFiles.Read("/proc/asound/cards").Split('\n')
            .Select(line => line.Split(" - ", 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[1].Trim());

    /// <summary>When the root file system was created, as the closest thing Linux has to an install date.</summary>
    private static DateTime? InstallDate()
    {
        foreach (var path in new[] { "/var/log/installer", "/etc/machine-id", "/lost+found" })
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    var created = File.GetCreationTime(path);
                    var changed = File.GetLastWriteTime(path);
                    var date = created.Year > 1980 && created < changed ? created : changed;
                    if (date.Year > 1980)
                    {
                        return date;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string FormatBiosDate(string? raw)
        => DateTime.TryParseExact(raw, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : Normalize(raw);

    private static string Join(IEnumerable<string?> values)
    {
        var distinct = values.Select(Normalize).Where(v => v != "-").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.Length == 0 ? "-" : string.Join(" | ", distinct);
    }
}
