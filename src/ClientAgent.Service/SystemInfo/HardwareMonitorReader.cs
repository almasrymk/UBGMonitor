using System.Diagnostics;
using System.Management;
using LibreHardwareMonitor.Hardware;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ClientAgent.Service.SystemInfo;

public sealed class HardwareMonitorReader : IDisposable
{
    private readonly ILogger<HardwareMonitorReader>? _logger;
    private readonly object _lock = new();
    private Computer? _computer;
    private bool _isOpen;
    private bool _loggedInventory;

    public HardwareMonitorReader(ILogger<HardwareMonitorReader>? logger = null)
    {
        _logger = logger;
    }

    public void Warmup()
    {
        lock (_lock)
        {
            EnsureOpen();
        }
    }

    public SensorsInfo Read()
    {
        var monitor = ReadMonitor();
        var (level, health, status) = ReadBattery();
        var acpiTemps = ReadAcpiTemps();
        var disk = ReadDiskFallback();

        return new SensorsInfo
        {
            CpuTempC = monitor.CpuTempC ?? acpiTemps.Cpu,
            GpuTempC = monitor.GpuTempC,
            MotherboardTempC = monitor.MotherboardTempC ?? acpiTemps.Motherboard,
            CpuFanRpm = monitor.CpuFanRpm ?? ReadWmiFanRpm(),
            GpuFanRpm = monitor.GpuFanRpm,
            CpuVoltage = monitor.CpuVoltage,
            Rail12V = monitor.Rail12V,
            Rail5V = monitor.Rail5V,
            Rail33V = monitor.Rail33V,
            PowerDrawW = monitor.PowerDrawW,
            BatteryLevelPercent = monitor.BatteryLevelPercent ?? level,
            BatteryStatus = status,
            BatteryHealthPercent = monitor.BatteryHealthPercent ?? health,
            DiskTempC = monitor.DiskTempC,
            DiskHealth = monitor.DiskHealth ?? disk.Health,
            DiskPowerOn = monitor.DiskPowerOn ?? disk.PowerOn
        };
    }

    private SensorsInfo ReadMonitor()
    {
        lock (_lock)
        {
            EnsureOpen();
            if (!_isOpen || _computer is null)
            {
                return new SensorsInfo();
            }

            try
            {
                UpdateAll();
                Thread.Sleep(50);
                UpdateAll();

                var readings = Flatten();
                if (!_loggedInventory)
                {
                    _loggedInventory = true;
                    _logger?.LogInformation(
                        "LibreHardwareMonitor sensors: {Count}. {Details}",
                        readings.Count,
                        string.Join(" | ", readings.Select(r => $"{r.Hw}/{r.Name}={r.Type}:{r.Value}")));
                }

                return MapReadings(readings);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "LibreHardwareMonitor update failed");
                return new SensorsInfo();
            }
        }
    }

    private void EnsureOpen()
    {
        if (_isOpen && _computer is not null)
        {
            return;
        }

        try
        {
            DisposeComputer();
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsBatteryEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = false
            };
            _computer.Open();
            _isOpen = true;
            _logger?.LogInformation("LibreHardwareMonitor opened. Hardware count: {Count}", _computer.Hardware.Count);
        }
        catch (Exception ex)
        {
            _isOpen = false;
            DisposeComputer();
            _logger?.LogWarning(ex, "LibreHardwareMonitor Open failed. Run the service as Administrator if sensors stay empty.");
            Debug.WriteLine($"LibreHardwareMonitor Open failed: {ex.Message}");
        }
    }

    private void UpdateAll()
    {
        if (_computer is null)
        {
            return;
        }

        foreach (var hardware in _computer.Hardware)
        {
            UpdateTree(hardware);
        }
    }

    private static void UpdateTree(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            UpdateTree(sub);
        }
    }

    private List<Reading> Flatten()
    {
        var list = new List<Reading>();
        if (_computer is null)
        {
            return list;
        }

        foreach (var hardware in _computer.Hardware)
        {
            CollectTree(hardware, hardware.HardwareType, list);
        }

        return list;
    }

    private static void CollectTree(IHardware hardware, HardwareType rootType, List<Reading> list)
    {
        var type = hardware.HardwareType == HardwareType.SuperIO ? rootType : hardware.HardwareType;
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is float value)
            {
                list.Add(new Reading(type, hardware.Name ?? string.Empty, sensor.SensorType, sensor.Name ?? string.Empty, value));
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            CollectTree(sub, rootType, list);
        }
    }

    private static SensorsInfo MapReadings(List<Reading> readings)
    {
        return new SensorsInfo
        {
            CpuTempC = PickTemp(readings, IsCpu, "package", "tctl", "tdie", "average", "cpu")
                       ?? Pick(readings, SensorType.Temperature, IsCpu),
            GpuTempC = PickTemp(readings, IsGpu, "gpu", "core", "hotspot")
                       ?? Pick(readings, SensorType.Temperature, IsGpu)
                       ?? PickByName(readings, SensorType.Temperature, "gpu"),
            MotherboardTempC = PickTemp(readings, IsBoard, "motherboard", "system", "ambient", "temp")
                               ?? Pick(readings, SensorType.Temperature, IsBoard, exclude: "cpu"),
            CpuFanRpm = PickFan(readings, "cpu")
                        ?? Pick(readings, SensorType.Fan, IsCpu)
                        ?? Pick(readings, SensorType.Fan, IsBoard),
            GpuFanRpm = PickFan(readings, "gpu")
                        ?? Pick(readings, SensorType.Fan, IsGpu),
            CpuVoltage = PickVolt(readings, "vcore", "vid", "cpu core", "cpu")
                         ?? Pick(readings, SensorType.Voltage, IsCpu),
            Rail12V = PickVolt(readings, "+12", "12v", "12 v"),
            Rail5V = PickVolt(readings, "+5v", "+5 v", "5v "),
            Rail33V = PickVolt(readings, "+3.3", "3.3v", "3v3", "3.3"),
            PowerDrawW = PickPower(readings, "package", "cores", "cpu")
                         ?? Pick(readings, SensorType.Power, IsCpu)
                         ?? Pick(readings, SensorType.Power, _ => true),
            BatteryLevelPercent = Pick(readings, SensorType.Level, t => t == HardwareType.Battery),
            DiskTempC = Pick(readings, SensorType.Temperature, t => t == HardwareType.Storage)
                        ?? PickByName(readings, SensorType.Temperature, "hdd", "ssd", "drive", "storage"),
            DiskHealth = FormatOptional(Pick(readings, SensorType.Level, t => t == HardwareType.Storage)
                                        ?? PickByName(readings, SensorType.Level, "life", "health", "remaining"), "{0:0}%"),
            DiskPowerOn = FormatStorageNamed(readings, "power on", "power-on", "hours")
        };
    }

    private static double? PickTemp(List<Reading> readings, Func<HardwareType, bool> hw, params string[] names)
        => PickByName(readings, SensorType.Temperature, names, hw);

    private static double? PickFan(List<Reading> readings, string name)
        => PickByName(readings, SensorType.Fan, [name]);

    private static double? PickVolt(List<Reading> readings, params string[] names)
        => PickByName(readings, SensorType.Voltage, names);

    private static double? PickPower(List<Reading> readings, params string[] names)
        => PickByName(readings, SensorType.Power, names, IsCpu);

    private static double? Pick(List<Reading> readings, SensorType type, Func<HardwareType, bool> hw, string? exclude = null)
        => readings.FirstOrDefault(r =>
            r.Type == type
            && hw(r.Hw)
            && (exclude is null || r.Name.Contains(exclude, StringComparison.OrdinalIgnoreCase) is false))?.Value;

    private static double? PickByName(List<Reading> readings, SensorType type, params string[] names)
        => PickByName(readings, type, names, _ => true);

    private static double? PickByName(List<Reading> readings, SensorType type, string[] names, Func<HardwareType, bool> hw)
    {
        foreach (var name in names)
        {
            var match = readings.FirstOrDefault(r =>
                r.Type == type
                && hw(r.Hw)
                && r.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Value;
            }
        }

        return null;
    }

    private static bool IsCpu(HardwareType t) => t == HardwareType.Cpu;

    private static bool IsGpu(HardwareType t)
        => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static bool IsBoard(HardwareType t)
        => t is HardwareType.Motherboard or HardwareType.SuperIO;

    private static (double? Cpu, double? Motherboard) ReadAcpiTemps()
    {
        var temps = new List<double>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["CurrentTemperature"] is null)
                    {
                        continue;
                    }

                    var celsius = Convert.ToDouble(obj["CurrentTemperature"]) / 10d - 273.15;
                    if (celsius is > 1 and < 150)
                    {
                        temps.Add(celsius);
                    }
                }
            }
        }
        catch
        {
            // ACPI thermal zone is missing on some machines.
        }

        return temps.Count switch
        {
            0 => (null, null),
            1 => (temps[0], null),
            _ => (temps[0], temps[1])
        };
    }

    private static double? ReadWmiFanRpm()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT DesiredSpeed FROM Win32_Fan");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["DesiredSpeed"] is null)
                    {
                        continue;
                    }

                    var rpm = Convert.ToDouble(obj["DesiredSpeed"]);
                    if (rpm >= 0)
                    {
                        return rpm;
                    }
                }
            }
        }
        catch
        {
            // Many systems do not expose Win32_Fan.
        }

        return null;
    }

    private static string? FormatOptional(double? value, string format)
        => value is null ? null : string.Format(format, value.Value);

    private static string? FormatStorageNamed(List<Reading> readings, params string[] names)
    {
        foreach (var name in names)
        {
            var match = readings.FirstOrDefault(r =>
                r.Hw == HardwareType.Storage
                && r.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Name.Contains("hour", StringComparison.OrdinalIgnoreCase)
                    ? $"{match.Value:0} h"
                    : $"{match.Value:0.##}";
            }
        }

        return null;
    }

    private static (string? Health, string? PowerOn) ReadDiskFallback()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Status FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var status = obj["Status"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        return (status, null);
                    }
                }
            }
        }
        catch
        {
            // Ignore disk WMI fallback failures.
        }

        return (null, null);
    }

    private static (double? Level, double? Health, string? Status) ReadBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT EstimatedChargeRemaining, DesignCapacity, FullChargeCapacity, BatteryStatus FROM Win32_Battery");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    double? level = obj["EstimatedChargeRemaining"] is null
                        ? null
                        : Convert.ToDouble(obj["EstimatedChargeRemaining"]);
                    double? health = null;
                    if (obj["DesignCapacity"] is not null && obj["FullChargeCapacity"] is not null)
                    {
                        var design = Convert.ToDouble(obj["DesignCapacity"]);
                        var full = Convert.ToDouble(obj["FullChargeCapacity"]);
                        if (design > 0)
                        {
                            health = Math.Round(full / design * 100, 0);
                        }
                    }

                    if (health is null)
                    {
                        health = ReadPortableBatteryHealth();
                    }

                    return (level, health, MapBatteryStatus(obj["BatteryStatus"]?.ToString()));
                }
            }
        }
        catch
        {
            // Desktops often have no battery.
        }

        return (null, ReadPortableBatteryHealth(), null);
    }

    private static string? MapBatteryStatus(string? raw) => raw switch
    {
        "1" => "Other",
        "2" => "Unknown",
        "3" => "Fully Charged",
        "4" => "Low",
        "5" => "Critical",
        "6" => "Charging",
        "7" => "Charging and High",
        "8" => "Charging and Low",
        "9" => "Charging and Critical",
        "10" => "Undefined",
        "11" => "Partially Charged",
        _ => string.IsNullOrWhiteSpace(raw) ? null : raw
    };

    private static double? ReadPortableBatteryHealth()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT DesignCapacity, FullChargedCapacity FROM Win32_PortableBattery");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["DesignCapacity"] is null || obj["FullChargedCapacity"] is null)
                    {
                        continue;
                    }

                    var design = Convert.ToDouble(obj["DesignCapacity"]);
                    var full = Convert.ToDouble(obj["FullChargedCapacity"]);
                    if (design > 0)
                    {
                        return Math.Round(full / design * 100, 0);
                    }
                }
            }
        }
        catch
        {
            // Optional class.
        }

        return null;
    }

    private void DisposeComputer()
    {
        try
        {
            if (_isOpen)
            {
                _computer?.Close();
            }
        }
        catch
        {
            // Ignore close failures.
        }

        _computer = null;
        _isOpen = false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeComputer();
        }
    }

    private sealed record Reading(HardwareType Hw, string HwName, SensorType Type, string Name, float Value);
}
