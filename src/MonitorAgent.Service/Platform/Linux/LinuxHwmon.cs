namespace MonitorAgent.Service.Platform.Linux;

/// <summary>The kernel's hardware monitors in /sys/class/hwmon (temperatures in millidegrees, fans in RPM, voltages in millivolts).</summary>
internal static class LinuxHwmon
{
    private static readonly string[] CpuChips = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal", "soc_thermal"];
    private static readonly string[] GpuChips = ["amdgpu", "radeon", "nouveau", "i915", "xe"];
    private static readonly string[] DiskChips = ["nvme", "drivetemp"];
    private static readonly string[] BoardChips = ["acpitz", "pch_", "nct", "it8", "f71", "w83"];

    public sealed record Chip(string Name, string Folder);

    public static List<Chip> Chips()
        => LinuxFiles.Directories("/sys/class/hwmon")
            .Select(folder => new Chip(LinuxFiles.ReadLine($"{folder}/name") ?? string.Empty, folder))
            .ToList();

    public static double? CpuTemperature(List<Chip> chips)
    {
        foreach (var chip in chips.Where(c => CpuChips.Any(name => c.Name.StartsWith(name, StringComparison.Ordinal))))
        {
            // coretemp labels the package "Package id 0"; k10temp calls it Tctl / Tdie; otherwise the first reading.
            var preferred = Temperature(chip, "Package") ?? Temperature(chip, "Tdie") ?? Temperature(chip, "Tctl") ?? Temperature(chip, null);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        foreach (var zone in LinuxFiles.Directories("/sys/class/thermal").Where(z => Path.GetFileName(z).StartsWith("thermal_zone", StringComparison.Ordinal)))
        {
            var type = LinuxFiles.ReadLine($"{zone}/type") ?? string.Empty;
            if (type is "x86_pkg_temp" or "cpu-thermal" or "cpu_thermal" or "soc-thermal")
            {
                return Milli(LinuxFiles.ReadLong($"{zone}/temp"));
            }
        }

        return null;
    }

    public static double? GpuTemperature(List<Chip> chips)
        => chips.Where(c => GpuChips.Contains(c.Name)).Select(c => Temperature(c, "edge") ?? Temperature(c, null)).FirstOrDefault(t => t is not null);

    public static double? GpuFan(List<Chip> chips)
        => chips.Where(c => GpuChips.Contains(c.Name)).Select(Fan).FirstOrDefault(f => f is not null);

    public static double? DiskTemperature(List<Chip> chips)
        => chips.Where(c => DiskChips.Contains(c.Name)).Select(c => Temperature(c, "Composite") ?? Temperature(c, null)).FirstOrDefault(t => t is not null);

    public static double? BoardTemperature(List<Chip> chips)
        => chips.Where(c => BoardChips.Any(name => c.Name.StartsWith(name, StringComparison.Ordinal)))
            .Select(c => Temperature(c, "SYSTIN") ?? Temperature(c, "Motherboard") ?? Temperature(c, null))
            .FirstOrDefault(t => t is not null);

    /// <summary>The first spinning fan on the board's sensor chip (usually the CPU fan header).</summary>
    public static double? CpuFan(List<Chip> chips)
        => chips.Where(c => !GpuChips.Contains(c.Name)).Select(Fan).FirstOrDefault(f => f is not null);

    /// <summary>A voltage whose label contains the text (Vcore, +12V...), in volts.</summary>
    public static double? Voltage(List<Chip> chips, params string[] labels)
    {
        foreach (var chip in chips)
        {
            foreach (var input in LinuxFiles.Files(chip.Folder, "in*_input"))
            {
                var label = LinuxFiles.ReadLine(input.Replace("_input", "_label")) ?? string.Empty;
                if (labels.Any(text => label.Contains(text, StringComparison.OrdinalIgnoreCase)) && LinuxFiles.ReadLong(input) is long millivolts)
                {
                    return Math.Round(millivolts / 1000d, 3);
                }
            }
        }

        return null;
    }

    private static double? Temperature(Chip chip, string? label)
    {
        foreach (var input in LinuxFiles.Files(chip.Folder, "temp*_input"))
        {
            if (label is not null)
            {
                var text = LinuxFiles.ReadLine(input.Replace("_input", "_label")) ?? string.Empty;
                if (!text.Contains(label, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (Milli(LinuxFiles.ReadLong(input)) is double celsius and > 0 and < 150)
            {
                return celsius;
            }
        }

        return null;
    }

    private static double? Fan(Chip chip)
        => LinuxFiles.Files(chip.Folder, "fan*_input").Select(LinuxFiles.ReadLong).FirstOrDefault(value => value > 0) is long rpm ? rpm : null;

    private static double? Milli(long? value) => value is null ? null : Math.Round(value.Value / 1000d, 1);
}
