using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.ViewModels;

public sealed partial class HardwareOsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isHardwareExpanded = true;
    [ObservableProperty] private bool _isOsExpanded;

    public ObservableCollection<InfoRowViewModel> HardwareRows { get; } =
    [
        new("Manufacturer"),
        new("Model"),
        new("Serial"),
        new("CPU"),
        new("Cores/Threads"),
        new("CPU Speed"),
        new("RAM"),
        new("GPU"),
        new("Disk"),
        new("Motherboard"),
        new("BIOS Version"),
        new("BIOS Date")
    ];

    public ObservableCollection<InfoRowViewModel> OsRows { get; } =
    [
        new("OS"),
        new("Version"),
        new("Build"),
        new("Arch"),
        new("Installed"),
        new("Last Boot"),
        new("Uptime"),
        new("Timezone"),
        new("Locale"),
        new("System Type")
    ];

    public void Update(HardwareInfo hardware, OsInfo os)
    {
        Set(HardwareRows, 0, hardware.Manufacturer);
        Set(HardwareRows, 1, hardware.Model);
        Set(HardwareRows, 2, hardware.SerialNumber);
        Set(HardwareRows, 3, hardware.Cpu);
        Set(HardwareRows, 4, hardware.CoresThreads);
        Set(HardwareRows, 5, hardware.CpuSpeed);
        Set(HardwareRows, 6, hardware.Ram);
        Set(HardwareRows, 7, string.IsNullOrWhiteSpace(hardware.Gpu) ? hardware.GpuModel : hardware.Gpu);
        Set(HardwareRows, 8, hardware.Disk);
        Set(HardwareRows, 9, hardware.Motherboard);
        Set(HardwareRows, 10, hardware.BiosVersion);
        Set(HardwareRows, 11, hardware.BiosDate);

        Set(OsRows, 0, os.Name);
        Set(OsRows, 1, os.Version);
        Set(OsRows, 2, os.Build);
        Set(OsRows, 3, os.Architecture);
        Set(OsRows, 4, os.InstallDate?.ToString("yyyy-MM-dd") ?? "-");
        Set(OsRows, 5, os.LastBoot?.ToString("yyyy-MM-dd") ?? "-");
        Set(OsRows, 6, FormatUptime(os.Uptime));
        Set(OsRows, 7, os.Timezone);
        Set(OsRows, 8, os.Locale);
        Set(OsRows, 9, os.SystemType);
    }

    private static void Set(ObservableCollection<InfoRowViewModel> rows, int index, string? value)
        => rows[index].Set(string.IsNullOrWhiteSpace(value) || value == "Unknown" ? "-" : value, SensorHealth.Ok);

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime <= TimeSpan.Zero)
        {
            return "-";
        }

        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }

        return $"{uptime.Hours}h {uptime.Minutes}m";
    }
}
