namespace MonitorAgent.Shared.Models;

public sealed class NetworkInfo
{
    public string ActiveInterface { get; init; } = "Unknown";

    public string Status { get; init; } = "Disconnected";

    public string IpAddress { get; init; } = string.Empty;

    public string PublicIp { get; init; } = string.Empty;

    public string MacAddress { get; init; } = string.Empty;

    public string Gateway { get; init; } = string.Empty;

    public string[] DnsServers { get; init; } = [];

    public double DownloadMbps { get; init; }

    public double UploadMbps { get; init; }

    public double TotalRxGB { get; init; }

    public double TotalTxGB { get; init; }

    public double? PingMs { get; init; }

    public double? PacketLossPercent { get; init; }

    /// <summary>"Wi-Fi", "Ethernet", "Mobile", or the raw adapter type.</summary>
    public string ConnectionType { get; init; } = "Unknown";

    public string AdapterName { get; init; } = string.Empty;

    public double? LinkSpeedMbps { get; init; }

    public string WifiSsid { get; init; } = string.Empty;

    public int? WifiSignalPercent { get; init; }

    public string WifiBand { get; init; } = string.Empty;

    public string WifiChannel { get; init; } = string.Empty;

    public string WifiRadioType { get; init; } = string.Empty;

    public double? WifiReceiveMbps { get; init; }

    public double? WifiTransmitMbps { get; init; }
}
