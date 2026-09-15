using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class HardwareOsViewModel : ObservableObject
{
    [ObservableProperty] private string _manufacturer = "-";
    [ObservableProperty] private string _model = "-";
    [ObservableProperty] private string _serial = "-";
    [ObservableProperty] private string _bios = "-";
    [ObservableProperty] private string _motherboard = "-";
    [ObservableProperty] private string _gpu = "-";
    [ObservableProperty] private string _osName = "-";
    [ObservableProperty] private string _version = "-";
    [ObservableProperty] private string _arch = "-";
    [ObservableProperty] private string _installed = "-";
    [ObservableProperty] private string _lastBoot = "-";
    [ObservableProperty] private string _timezone = "-";

    public void Update(HardwareInfo hardware, OsInfo os)
    {
        Manufacturer = hardware.Manufacturer;
        Model = hardware.Model;
        Serial = hardware.SerialNumber;
        Bios = hardware.BiosVersion;
        Motherboard = hardware.Motherboard;
        Gpu = hardware.GpuVramGB > 0 ? $"{hardware.GpuModel} ({hardware.GpuVramGB:0}GB)" : hardware.GpuModel;
        OsName = os.Name;
        Version = os.Version;
        Arch = os.Architecture;
        Installed = os.InstallDate?.ToString("yyyy-MM-dd") ?? "-";
        LastBoot = os.LastBoot?.ToString("yyyy-MM-dd") ?? "-";
        Timezone = os.Timezone;
    }
}
