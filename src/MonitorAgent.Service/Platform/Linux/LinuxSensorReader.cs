using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>Sensors from /sys/class/hwmon and the battery from /sys/class/power_supply.</summary>
public sealed class LinuxSensorReader : ISensorReader
{
    public void Warmup()
    {
    }

    public SensorsInfo Read()
    {
        var chips = LinuxHwmon.Chips();
        var battery = LinuxFiles.Directories("/sys/class/power_supply")
            .FirstOrDefault(folder => LinuxFiles.ReadLine($"{folder}/type") == "Battery");
        var mains = LinuxFiles.Directories("/sys/class/power_supply")
            .Where(folder => LinuxFiles.ReadLine($"{folder}/type") == "Mains")
            .Any(folder => LinuxFiles.ReadLine($"{folder}/online") == "1");

        return new SensorsInfo
        {
            CpuTempC = LinuxHwmon.CpuTemperature(chips),
            GpuTempC = LinuxHwmon.GpuTemperature(chips),
            MotherboardTempC = LinuxHwmon.BoardTemperature(chips),
            CpuFanRpm = LinuxHwmon.CpuFan(chips),
            GpuFanRpm = LinuxHwmon.GpuFan(chips),
            CpuVoltage = LinuxHwmon.Voltage(chips, "Vcore", "VCORE", "CPU Core"),
            Rail12V = LinuxHwmon.Voltage(chips, "+12V", "12V"),
            Rail5V = LinuxHwmon.Voltage(chips, "+5V", "5VCC"),
            Rail33V = LinuxHwmon.Voltage(chips, "+3.3V", "3VCC", "3.3V"),
            PowerDrawW = battery is null ? null : PowerDraw(battery),
            BatteryLevelPercent = battery is null ? null : LinuxFiles.ReadLong($"{battery}/capacity"),
            BatteryStatus = battery is null ? null : BatteryStatus(LinuxFiles.ReadLine($"{battery}/status"), mains),
            BatteryHealthPercent = battery is null ? null : BatteryHealth(battery),
            DiskTempC = LinuxHwmon.DiskTemperature(chips)
        };
    }

    public static string BatteryStatus(string? status, bool pluggedIn) => status switch
    {
        "Charging" => "Charging",
        "Discharging" => "Discharging",
        "Full" => "Fully Charged",
        "Not charging" => "Plugged in",
        _ => pluggedIn ? "Plugged in" : "-"
    };

    private static double? PowerDraw(string battery)
    {
        if (LinuxFiles.ReadLong($"{battery}/power_now") is long microwatts)
        {
            return Math.Round(microwatts / 1_000_000d, 1);
        }

        return LinuxFiles.ReadLong($"{battery}/current_now") is long microamps && LinuxFiles.ReadLong($"{battery}/voltage_now") is long microvolts
            ? Math.Round(microamps / 1_000_000d * (microvolts / 1_000_000d), 1)
            : null;
    }

    private static double? BatteryHealth(string battery)
    {
        var full = LinuxFiles.ReadLong($"{battery}/energy_full") ?? LinuxFiles.ReadLong($"{battery}/charge_full");
        var design = LinuxFiles.ReadLong($"{battery}/energy_full_design") ?? LinuxFiles.ReadLong($"{battery}/charge_full_design");
        return full is > 0 && design is > 0 ? Math.Round(Math.Min(100, full.Value * 100d / design.Value), 0) : null;
    }
}
