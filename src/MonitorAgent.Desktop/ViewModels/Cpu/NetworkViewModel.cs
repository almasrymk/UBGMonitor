using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class NetworkViewModel : ObservableObject
{
    [ObservableProperty] private string _activeInterface = "Unknown";
    [ObservableProperty] private string _statusLine = "-";
    [ObservableProperty] private string _ipAddress = "-";
    [ObservableProperty] private string _publicIp = "-";
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
    [ObservableProperty] private double _downloadMbps;
    [ObservableProperty] private double _uploadMbps;
    [ObservableProperty] private double _gaugeMaximum = 100;
    [ObservableProperty] private string _throughputText = "0.0 Mbps | 0.0 Mbps";
    [ObservableProperty] private string _connectionType = "Unknown";
    [ObservableProperty] private bool _isWifi;
    [ObservableProperty] private bool _isCable;
    [ObservableProperty] private string _connectionIcon = "\uE774";
    [ObservableProperty] private string _connectionText = "-";
    [ObservableProperty] private string _connectionToolTip = "-";
    [ObservableProperty] private int? _wifiSignalPercent;
    [ObservableProperty] private double? _linkSpeedMbps;
    [ObservableProperty] private string _speedTestText = "Scheduled";
    [ObservableProperty] private string _speedTestToolTip = "Runs automatically based on General settings";
    [ObservableProperty] private bool _speedTestEnabled = true;
    [ObservableProperty] private double? _testedDownloadMbps;
    [ObservableProperty] private double? _testedUploadMbps;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSpeedTestCommand))]
    private bool _isSpeedTestRunning;

    private bool _internetReachable = true;
    private NetworkInfo? _lastNetwork;

    /// <summary>Keeps the connection text in line with the app-wide internet check (adapter up does not mean internet works).</summary>
    public void SetInternetReachable(bool reachable)
    {
        if (_internetReachable == reachable)
        {
            return;
        }

        _internetReachable = reachable;
        if (_lastNetwork is not null)
        {
            UpdateConnection(_lastNetwork);
        }
    }

    public string AdapterName { get; private set; } = string.Empty;

    /// <summary>Received throughput in Kbps.</summary>
    public ObservableCollection<double> History { get; } = [];

    /// <summary>Sent throughput in Kbps.</summary>
    public ObservableCollection<double> UploadHistory { get; } = [];

    public void Reset()
    {
        ActiveInterface = "Unknown";
        StatusLine = "-";
        IpAddress = "-";
        PublicIp = "-";
        MacAddress = "-";
        Gateway = "-";
        Dns = "-";
        Download = "-";
        Upload = "-";
        TotalRx = "-";
        TotalTx = "-";
        Ping = "-";
        PacketLoss = "0%";
        Status = "Unknown";
        DownloadMbps = 0;
        UploadMbps = 0;
        GaugeMaximum = 100;
        ThroughputText = "0.0 Mbps | 0.0 Mbps";
        ConnectionType = "Unknown";
        IsWifi = false;
        IsCable = false;
        ConnectionIcon = "\uE774";
        ConnectionText = "-";
        ConnectionToolTip = "-";
        WifiSignalPercent = null;
        LinkSpeedMbps = null;
        TestedDownloadMbps = null;
        TestedUploadMbps = null;
        IsSpeedTestRunning = false;
        SpeedTestText = SpeedTestEnabled ? "Scheduled" : "Off";
        SpeedTestToolTip = "Runs automatically based on General settings";
        AdapterName = string.Empty;
        History.Clear();
        UploadHistory.Clear();
        _lastNetwork = null;
        _internetReachable = true;
    }

    private void ApplyThroughput(double downloadMbps, double uploadMbps)
    {
        Download = FormatMbps(downloadMbps);
        Upload = FormatMbps(uploadMbps);
        DownloadMbps = downloadMbps;
        UploadMbps = uploadMbps;
        var peak = Math.Max(downloadMbps, uploadMbps);
        var niceMax = NiceMaximum(peak);
        if (niceMax > GaugeMaximum || peak < GaugeMaximum * 0.12)
        {
            GaugeMaximum = niceMax;
        }

        ThroughputText = $"↓ {FormatMbps(downloadMbps)}  ↑ {FormatMbps(uploadMbps)}";
    }

    public void Update(NetworkInfo network)
    {
        ActiveInterface = network.ActiveInterface;
        Status = network.Status;
        StatusLine = $"Interface: {network.ActiveInterface} | Status: ● {network.Status} | Speed: {network.DownloadMbps:0.##} Mbps";
        IpAddress = string.IsNullOrWhiteSpace(network.IpAddress) ? "-" : network.IpAddress;
        PublicIp = string.IsNullOrWhiteSpace(network.PublicIp) ? "-" : network.PublicIp;
        MacAddress = string.IsNullOrWhiteSpace(network.MacAddress) ? "-" : network.MacAddress;
        Gateway = string.IsNullOrWhiteSpace(network.Gateway) ? "-" : network.Gateway;
        Dns = network.DnsServers.Length == 0 ? "-" : string.Join(", ", network.DnsServers.Take(2));
        AdapterName = network.AdapterName;
        ApplyThroughput(network.DownloadMbps, network.UploadMbps);
        CpuViewModel.Push(History, network.DownloadMbps * 1000d);
        CpuViewModel.Push(UploadHistory, network.UploadMbps * 1000d);

        TotalRx = $"{network.TotalRxGB:0.0} GB";
        TotalTx = $"{network.TotalTxGB:0.0} GB";
        Ping = network.PingMs.HasValue ? $"{network.PingMs:0} ms" : "N/A";
        PacketLoss = network.PacketLossPercent.HasValue ? $"{network.PacketLossPercent:0}%" : "N/A";
        UpdateConnection(network);
    }

    private void UpdateConnection(NetworkInfo network)
    {
        _lastNetwork = network;
        var up = string.Equals(network.Status, "Up", StringComparison.OrdinalIgnoreCase);
        ConnectionType = network.ConnectionType;
        IsWifi = up && network.ConnectionType == "Wi-Fi";
        IsCable = up && network.ConnectionType == "Ethernet";
        LinkSpeedMbps = network.LinkSpeedMbps;
        WifiSignalPercent = IsWifi ? network.WifiSignalPercent : null;

        if (!up)
        {
            ConnectionIcon = "\uF384";
            ConnectionText = "Disconnected";
            ConnectionToolTip = "No active network connection";
            return;
        }

        var link = network.LinkSpeedMbps is double speed ? FormatLink(speed) : "-";
        if (IsWifi)
        {
            ConnectionIcon = "\uE701";
            var ssid = string.IsNullOrWhiteSpace(network.WifiSsid) ? "Wi-Fi" : network.WifiSsid;
            var signal = network.WifiSignalPercent is int percent ? $" · {percent}%" : string.Empty;
            ConnectionText = $"Wi-Fi: {ssid}{signal}";
            ConnectionToolTip = string.Join(Environment.NewLine, new[]
            {
                $"Network: {ssid}",
                network.WifiSignalPercent is int s ? $"Signal: {s}%" : null,
                string.IsNullOrWhiteSpace(network.WifiBand) ? null : $"Band: {network.WifiBand} (channel {network.WifiChannel})",
                string.IsNullOrWhiteSpace(network.WifiRadioType) ? null : $"Radio: {network.WifiRadioType}",
                network.WifiReceiveMbps is double rx ? $"Receive rate: {rx:0} Mbps" : null,
                network.WifiTransmitMbps is double tx ? $"Transmit rate: {tx:0} Mbps" : null,
                $"Adapter: {network.AdapterName}",
                $"Gateway: {Gateway}",
                $"DNS: {Dns}"
            }.Where(line => line is not null));
        }
        else if (IsCable)
        {
            ConnectionIcon = "\uE839";
            ConnectionText = $"Cable: {link}";
            ConnectionToolTip = string.Join(Environment.NewLine,
                "Ethernet (cable)", $"Link speed: {link}", $"Adapter: {network.AdapterName}", $"Gateway: {Gateway}", $"DNS: {Dns}");
        }
        else
        {
            ConnectionIcon = "\uE774";
            ConnectionText = $"{network.ConnectionType}: {link}";
            ConnectionToolTip = string.Join(Environment.NewLine,
                $"Adapter: {network.AdapterName}", $"Gateway: {Gateway}", $"DNS: {Dns}");
        }

        if (!_internetReachable)
        {
            ConnectionText += " · No internet";
            ConnectionToolTip = "Connected to the network, but the internet cannot be reached."
                                + Environment.NewLine + Environment.NewLine + ConnectionToolTip;
        }
    }

    /// <summary>Asks the Agent service to run a speed test; the service runs it and reports through <see cref="ApplyInternetState"/>.</summary>
    public Func<Task<InternetStateDto?>>? StartSpeedTest { get; set; }

    [RelayCommand(CanExecute = nameof(CanRunSpeedTest))]
    private async Task RunSpeedTestAsync()
    {
        IsSpeedTestRunning = true;
        SpeedTestText = "Starting...";
        var state = StartSpeedTest is null ? null : await StartSpeedTest();
        if (state is null)
        {
            IsSpeedTestRunning = false;
            SpeedTestText = "Failed";
            SpeedTestToolTip = "The Agent service did not respond, so the speed test could not start.";
            return;
        }

        state.SpeedTestRunning = true;
        ApplyInternetState(state);
    }

    private bool CanRunSpeedTest() => !IsSpeedTestRunning;

    /// <summary>Shows the speed test state measured by the service.</summary>
    public void ApplyInternetState(InternetStateDto state)
    {
        IsSpeedTestRunning = state.SpeedTestRunning;
        if (state.SpeedTestRunning)
        {
            SpeedTestText = state.SpeedTestProgress ?? "Testing...";
            return;
        }

        TestedDownloadMbps = state.DownloadMbps;
        TestedUploadMbps = state.UploadMbps;
        if (state.DownloadMbps is double download)
        {
            var upload = state.UploadMbps is double up ? FormatMbps(up) : "-";
            SpeedTestText = $"↓ {FormatMbps(download)}  ↑ {upload}";
            SpeedTestToolTip = $"Last speed test at {state.SpeedTestCompletedAt:HH:mm}{Environment.NewLine}"
                               + $"Download: {FormatMbps(download)}{Environment.NewLine}"
                               + $"Upload: {(state.UploadMbps is null ? "failed (server unavailable)" : upload)}"
                               + (state.SpeedTestError is null ? string.Empty : $"{Environment.NewLine}Last attempt failed: {state.SpeedTestError}");
        }
        else if (state.SpeedTestError is not null)
        {
            SpeedTestText = "Failed";
            SpeedTestToolTip = $"Speed test failed: {state.SpeedTestError}";
        }
        else
        {
            SpeedTestText = SpeedTestEnabled ? "Scheduled" : "Off";
        }
    }

    partial void OnSpeedTestEnabledChanged(bool value)
    {
        if (TestedDownloadMbps is null && !IsSpeedTestRunning)
        {
            SpeedTestText = value ? "Scheduled" : "Off";
        }
    }

    private static string FormatMbps(double mbps) => mbps switch
    {
        >= 1000 => $"{mbps / 1000d:0.00} Gbps",
        >= 1 => $"{mbps:0.0} Mbps",
        _ => $"{mbps * 1000d:0} Kbps"
    };

    private static string FormatLink(double mbps)
        => mbps >= 1000 ? $"{mbps / 1000d:0.#} Gbps" : $"{mbps:0} Mbps";

    private static double NiceMaximum(double value)
    {
        var padded = Math.Max(value * 1.35, 1d);
        foreach (var step in new[] { 1d, 5d, 10d, 25d, 50d, 100d, 250d, 500d, 1000d, 2500d, 10000d })
        {
            if (padded <= step)
            {
                return step;
            }
        }

        return 10000d;
    }
}
