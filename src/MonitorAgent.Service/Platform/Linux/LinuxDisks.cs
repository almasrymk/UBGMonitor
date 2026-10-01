using System.Collections.Concurrent;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>The physical disks in /sys/block and the folders mounted from each.</summary>
internal static class LinuxDisks
{
    private static readonly TimeSpan SmartTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (string Status, DateTime AtUtc)> SmartCache = new();

    /// <summary>File systems that hold real data; everything else in /proc/mounts is virtual (proc, tmpfs, overlay...).</summary>
    public static readonly HashSet<string> DataFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "jfs", "reiserfs", "vfat", "exfat", "ntfs", "ntfs3", "fuseblk", "bcachefs"
    };

    public static IEnumerable<string> DiskNames()
        => LinuxFiles.Directories("/sys/block")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(LinuxParsers.IsPhysicalDiskName)
            .Where(name => LinuxFiles.ReadLong($"/sys/block/{name}/size") > 0);

    public static List<PhysicalDisk> Read()
    {
        var mountsByDisk = MountPointsByDisk();
        var disks = new List<PhysicalDisk>();
        foreach (var name in DiskNames())
        {
            var dir = $"/sys/block/{name}";
            var rotational = LinuxFiles.ReadLine($"{dir}/queue/rotational") == "1";
            var removable = LinuxFiles.ReadLine($"{dir}/removable") == "1";
            disks.Add(new PhysicalDisk
            {
                Index = disks.Count,
                Model = LinuxFiles.ReadLine($"{dir}/device/model") ?? LinuxFiles.ReadLine($"{dir}/device/name") ?? name,
                Type = removable ? "Removable" : rotational ? "HDD" : "SSD",
                Interface = Interface(name),
                SizeGB = Measure.Round(Measure.BytesToGb((LinuxFiles.ReadLong($"{dir}/size") ?? 0) * 512d)),
                SmartStatus = SmartStatus(name),
                HealthPercent = null,
                TemperatureC = null,
                Partitions = mountsByDisk.TryGetValue(name, out var mounts) ? mounts : []
            });
        }

        return disks;
    }

    public static string Interface(string name)
    {
        if (name.StartsWith("nvme", StringComparison.Ordinal))
        {
            return "NVMe";
        }

        if (name.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            return "MMC";
        }

        if (name.StartsWith("vd", StringComparison.Ordinal) || name.StartsWith("xvd", StringComparison.Ordinal))
        {
            return "Virtual";
        }

        var device = ResolveLink($"/sys/block/{name}");
        return device.Contains("/usb", StringComparison.Ordinal) ? "USB" : "SATA/SCSI";
    }

    /// <summary>"OK" / "FAILING" from smartctl when it is installed (cached; checking a disk takes a moment).</summary>
    private static string SmartStatus(string name)
    {
        if (SmartCache.TryGetValue(name, out var cached) && DateTime.UtcNow - cached.AtUtc < SmartTtl)
        {
            return cached.Status;
        }

        var result = Command.Run("smartctl", $"-H /dev/{name}", 5000);
        var status = result is null
            ? "Unknown"
            : result.Output.Contains("PASSED", StringComparison.OrdinalIgnoreCase) || result.Output.Contains(": OK", StringComparison.OrdinalIgnoreCase)
                ? "OK"
                : result.Output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ? "FAILING" : "Unknown";
        SmartCache[name] = (status, DateTime.UtcNow);
        return status;
    }

    /// <summary>The mount points on each disk, following partitions and LVM / LUKS mappers back to the disk.</summary>
    private static Dictionary<string, List<string>> MountPointsByDisk()
    {
        var diskOfBlock = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var disk in DiskNames())
        {
            diskOfBlock[disk] = disk;
            foreach (var partition in LinuxFiles.Directories($"/sys/block/{disk}").Select(Path.GetFileName).OfType<string>())
            {
                if (partition.StartsWith(disk, StringComparison.Ordinal))
                {
                    diskOfBlock[partition] = disk;
                }
            }
        }

        foreach (var mapper in LinuxFiles.Directories("/sys/block").Select(Path.GetFileName).OfType<string>().Where(n => n.StartsWith("dm-", StringComparison.Ordinal)))
        {
            var slave = LinuxFiles.Directories($"/sys/block/{mapper}/slaves").Select(Path.GetFileName).OfType<string>()
                .FirstOrDefault(diskOfBlock.ContainsKey);
            if (slave is not null)
            {
                diskOfBlock[mapper] = diskOfBlock[slave];
            }
        }

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (device, mountPoint, fileSystem) in LinuxParsers.ParseMounts(LinuxFiles.Read("/proc/mounts")))
        {
            if (!device.StartsWith("/dev/", StringComparison.Ordinal) || !DataFileSystems.Contains(fileSystem))
            {
                continue;
            }

            var block = Path.GetFileName(ResolveLink(device));
            if (diskOfBlock.TryGetValue(block, out var disk))
            {
                var list = result.TryGetValue(disk, out var existing) ? existing : result[disk] = [];
                if (!list.Contains(mountPoint))
                {
                    list.Add(mountPoint);
                }
            }
        }

        return result;
    }

    private static string ResolveLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}
