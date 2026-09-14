using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Converters;

namespace ClientAgent.UI.ViewModels;

public sealed partial class CpuViewModel : ObservableObject
{
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _model = "Unknown";
    [ObservableProperty] private int _physicalCores;
    [ObservableProperty] private int _logicalCores;
    [ObservableProperty] private string _coresText = "-";
    [ObservableProperty] private string _speedText = "-";
    [ObservableProperty] private string _temperature = "N/A";
    [ObservableProperty] private int _processCount;
    public ObservableCollection<double> History { get; } = [];

    public void Update(CpuInfo cpu)
    {
        UsagePercent = cpu.UsagePercent;
        Model = cpu.Model;
        PhysicalCores = cpu.PhysicalCores;
        LogicalCores = cpu.LogicalCores;
        CoresText = $"{cpu.LogicalCores} ({cpu.PhysicalCores}P)";
        SpeedText = $"{cpu.CurrentSpeedGhz:0.0} GHz (Max {cpu.MaxSpeedGhz:0.0})";
        Temperature = cpu.TemperatureC.HasValue ? $"{cpu.TemperatureC:0}°C" : "N/A";
        ProcessCount = cpu.ProcessCount;
        Push(History, cpu.UsagePercent);
    }

    internal static void Push(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > 20)
        {
            history.RemoveAt(0);
        }
    }
}

public sealed partial class RamViewModel : ObservableObject
{
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private double _totalGb;
    [ObservableProperty] private double _usedGb;
    [ObservableProperty] private double _freeGb;
    [ObservableProperty] private double _cachedGb;
    public ObservableCollection<double> History { get; } = [];

    public void Update(RamInfo ram)
    {
        UsagePercent = ram.UsagePercent;
        TotalGb = ram.TotalGB;
        UsedGb = ram.UsedGB;
        FreeGb = ram.FreeGB;
        CachedGb = ram.CachedGB;
        CpuViewModel.Push(History, ram.UsagePercent);
    }
}

public sealed partial class NetworkViewModel : ObservableObject
{
    [ObservableProperty] private string _activeInterface = "Unknown";
    [ObservableProperty] private string _statusLine = "-";
    [ObservableProperty] private string _ipAddress = "-";
    [ObservableProperty] private string _macAddress = "-";
    [ObservableProperty] private string _gateway = "-";
    [ObservableProperty] private string _dns = "-";
    [ObservableProperty] private string _download = "-";
    [ObservableProperty] private string _upload = "-";
    [ObservableProperty] private string _totalRx = "-";
    [ObservableProperty] private string _totalTx = "-";
    [ObservableProperty] private string _ping = "-";
    [ObservableProperty] private string _packetLoss = "0%";
    [ObservableProperty] private string _status = "Unknown";
    public ObservableCollection<double> History { get; } = [];

    public void Update(NetworkInfo network)
    {
        ActiveInterface = network.ActiveInterface;
        Status = network.Status;
        StatusLine = $"Interface: {network.ActiveInterface} | Status: ● {network.Status} | Speed: {network.DownloadMbps:0.##} Mbps";
        IpAddress = string.IsNullOrWhiteSpace(network.IpAddress) ? "-" : network.IpAddress;
        MacAddress = string.IsNullOrWhiteSpace(network.MacAddress) ? "-" : network.MacAddress;
        Gateway = string.IsNullOrWhiteSpace(network.Gateway) ? "-" : network.Gateway;
        Dns = network.DnsServers.Length == 0 ? "-" : string.Join(", ", network.DnsServers.Take(2));
        Download = $"{network.DownloadMbps:0.0} Mbps";
        Upload = $"{network.UploadMbps:0.0} Mbps";
        TotalRx = $"{network.TotalRxGB:0.0} GB";
        TotalTx = $"{network.TotalTxGB:0.0} GB";
        Ping = network.PingMs.HasValue ? $"{network.PingMs:0} ms" : "N/A";
        PacketLoss = network.PingMs.HasValue ? "0%" : "N/A";
        CpuViewModel.Push(History, network.DownloadMbps);
    }
}

public sealed partial class DiskRowViewModel : ObservableObject
{
    [ObservableProperty] private string _drive = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _fileSystem = string.Empty;
    [ObservableProperty] private string _total = string.Empty;
    [ObservableProperty] private string _used = string.Empty;
    [ObservableProperty] private string _free = string.Empty;
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _status = "Healthy";
}

public sealed partial class PhysicalDiskRowViewModel : ObservableObject
{
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private double _healthPercent = 100;
    [ObservableProperty] private string _temperature = "N/A";
    [ObservableProperty] private string _smartStatus = "Unknown";
}

public sealed partial class DiskViewModel : ObservableObject
{
    public ObservableCollection<DiskRowViewModel> Partitions { get; } = [];
    public ObservableCollection<PhysicalDiskRowViewModel> PhysicalDisks { get; } = [];

    public void Update(IEnumerable<DiskPartition> partitions, IEnumerable<PhysicalDisk> disks)
    {
        Partitions.Clear();
        foreach (var p in partitions)
        {
            Partitions.Add(new DiskRowViewModel
            {
                Drive = p.DriveLetter.TrimEnd('\\'),
                Label = string.IsNullOrWhiteSpace(p.Label) ? "-" : p.Label,
                FileSystem = p.FileSystem,
                Total = FormatGb(p.TotalGB),
                Used = FormatGb(p.UsedGB),
                Free = FormatGb(p.FreeGB),
                UsagePercent = p.UsagePercent,
                Status = DiskStatusHelper.FromUsage(p.UsagePercent)
            });
        }

        PhysicalDisks.Clear();
        foreach (var d in disks)
        {
            PhysicalDisks.Add(new PhysicalDiskRowViewModel
            {
                Model = d.Model,
                Type = $"{d.Type} / {d.Interface}",
                HealthPercent = d.HealthPercent ?? 100,
                Temperature = d.TemperatureC.HasValue ? $"{d.TemperatureC:0}°C" : "N/A",
                SmartStatus = d.SmartStatus
            });
        }
    }

    private static string FormatGb(double gb)
        => gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
}

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
