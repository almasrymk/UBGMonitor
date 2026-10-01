namespace MonitorAgent.Service.Platform;

/// <summary>Reads the fixed hardware and system details of this computer (refreshed every Hardware / OS interval).</summary>
public interface IInventoryCollector
{
    HardwareInventory Collect();
}

/// <summary>The fixed hardware and system details shown in the Hardware / OS levels, already formatted.</summary>
public sealed class HardwareInventory
{
    /// <summary>"Windows", "Linux" or "macOS": the start of the system rows' names ("Windows Edition"...).</summary>
    public string OsFamily { get; init; } = "Windows";
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
    public string OsEdition { get; init; } = "Unknown";
    public string OsVersion { get; init; } = "Unknown";
    public string OsBuild { get; init; } = "Unknown";
    public string ProductKey { get; init; } = "Unknown";
    public string Architecture { get; init; } = "Unknown";
    public string GpuCombined { get; init; } = "Unknown";
    public string RamCombined { get; init; } = "Unknown";
    public string DiskCombined { get; init; } = "Unknown";
    public string CoresThreads { get; init; } = "Unknown";
    public double GpuVramGb { get; init; }
}

/// <summary>Text formatting shared by the inventory collectors.</summary>
public static class InventoryText
{
    public static string Text(string? value, string fallback = "Unknown", bool emptyAsUnknown = true)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return emptyAsUnknown ? fallback : string.Empty;
    }

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value is "Unknown" or "unknown" ? "-" : value.Trim();

    public static string FormatGhz(double mhz)
        => mhz > 0 ? $"{mhz / 1000d:0.0} GHz" : "-";

    public static string FormatMac(string? raw)
    {
        var value = raw?.Replace("-", string.Empty).Replace(":", string.Empty).Trim() ?? string.Empty;
        if (value.Length != 12)
        {
            return Normalize(raw);
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => value.Substring(i * 2, 2)));
    }

    public static string FormatCacheKb(int kb)
    {
        if (kb <= 0)
        {
            return "-";
        }

        return kb >= 1024 ? $"{kb / 1024d:0} MB" : $"{kb} KB";
    }
}
