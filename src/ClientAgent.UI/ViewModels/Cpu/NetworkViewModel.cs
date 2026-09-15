using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

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
