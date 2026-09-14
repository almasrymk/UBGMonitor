using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    [ObservableProperty] private string _activeInterface = "Unknown";
    [ObservableProperty] private string _ipAddress = "-";
    [ObservableProperty] private string _macAddress = "-";
    [ObservableProperty] private string _gateway = "-";
    [ObservableProperty] private string _speed = "-";
    [ObservableProperty] private string _status = "Unknown";

    public void Update(NetworkInfo network)
    {
        ActiveInterface = network.ActiveInterface;
        IpAddress = string.IsNullOrWhiteSpace(network.IpAddress) ? "-" : network.IpAddress;
        MacAddress = string.IsNullOrWhiteSpace(network.MacAddress) ? "-" : network.MacAddress;
        Gateway = string.IsNullOrWhiteSpace(network.Gateway) ? "-" : network.Gateway;
        Speed = $"{network.DownloadMbps:0.0} Mbps";
        Status = network.Status;
    }
}
