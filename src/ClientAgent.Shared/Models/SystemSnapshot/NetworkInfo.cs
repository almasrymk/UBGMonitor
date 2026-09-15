namespace ClientAgent.Shared.Models;

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
}
