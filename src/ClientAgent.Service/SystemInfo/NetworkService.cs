using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.SystemInfo;

public interface INetworkService
{
    Task<NetworkInfo> GetNetworkAsync(CancellationToken cancellationToken = default);

    Task<HardwareLevelDto> GetLevelAsync(CancellationToken cancellationToken = default);
}

public sealed class NetworkService : INetworkService
{
    private static readonly IPAddress PingTarget = IPAddress.Parse("8.8.8.8");
    private static readonly TimeSpan PingCacheTtl = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PublicIpCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly HttpClient PublicIpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly string[] PublicIpUrls =
    [
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com"
    ];

    private readonly ILogger<NetworkService> _logger;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pingGate = new(1, 1);
    private string? _lastInterfaceId;
    private long _lastRx;
    private long _lastTx;
    private long _lastTimestamp;
    private double _downloadMbps;
    private double _uploadMbps;
    private double? _cachedPingMs;
    private double? _cachedLossPercent;
    private DateTime _pingAtUtc;
    private string? _cachedPublicIp;
    private DateTime _publicIpAtUtc;
    private readonly SemaphoreSlim _publicIpGate = new(1, 1);

    public NetworkService(ILogger<NetworkService> logger)
    {
        _logger = logger;
    }

    public Task<NetworkInfo> GetNetworkAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _ = RefreshPingIfStaleAsync(wait: false);
        _ = RefreshPublicIpIfStaleAsync(wait: false);
        return Task.FromResult(MapNetwork(ReadSample()));
    }

    public async Task<HardwareLevelDto> GetLevelAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            RefreshPingIfStaleAsync(wait: true),
            RefreshPublicIpIfStaleAsync(wait: true)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return MapLevel(ReadSample());
    }

    private Sample ReadSample()
    {
        try
        {
            var nic = PickActiveInterface();
            if (nic is null)
            {
                return new Sample();
            }

            var props = nic.GetIPProperties();
            var stats = nic.GetIPStatistics();
            var ipv4 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            var ipv6 = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                                     && !a.Address.IsIPv6LinkLocal);

            double download;
            double upload;
            lock (_lock)
            {
                var now = Stopwatch.GetTimestamp();
                var id = nic.Id;
                if (_lastTimestamp > 0 && _lastInterfaceId == id)
                {
                    var elapsed = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
                    if (elapsed > 0.05)
                    {
                        var rxDelta = stats.BytesReceived - _lastRx;
                        var txDelta = stats.BytesSent - _lastTx;
                        if (rxDelta < 0) rxDelta = 0;
                        if (txDelta < 0) txDelta = 0;
                        _downloadMbps = rxDelta * 8d / elapsed / 1_000_000d;
                        _uploadMbps = txDelta * 8d / elapsed / 1_000_000d;
                    }
                }

                _lastInterfaceId = id;
                _lastRx = stats.BytesReceived;
                _lastTx = stats.BytesSent;
                _lastTimestamp = now;
                download = Math.Round(_downloadMbps, 2);
                upload = Math.Round(_uploadMbps, 2);
            }

            bool dhcp;
            try
            {
                dhcp = props.GetIPv4Properties().IsDhcpEnabled;
            }
            catch
            {
                dhcp = false;
            }

            return new Sample
            {
                Hostname = Environment.MachineName,
                Domain = Environment.UserDomainName,
                ConnectionType = nic.NetworkInterfaceType.ToString(),
                Status = nic.OperationalStatus.ToString(),
                InterfaceName = nic.Name,
                AdapterModel = nic.Description,
                MacAddress = FormatMac(nic.GetPhysicalAddress().ToString()),
                LinkSpeed = FormatLinkSpeed(nic.Speed),
                Ipv4 = ipv4?.Address.ToString() ?? string.Empty,
                Ipv6 = ipv6?.Address.ToString() ?? string.Empty,
                SubnetMask = ipv4?.IPv4Mask?.ToString() ?? string.Empty,
                Gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?.ToString() ?? string.Empty,
                Dns = string.Join(", ", props.DnsAddresses.Take(3).Select(a => a.ToString())),
                DhcpEnabled = dhcp ? "Yes" : "No",
                DownloadMbps = download,
                UploadMbps = upload,
                TotalRxGb = Math.Round(stats.BytesReceived / 1024d / 1024d / 1024d, 2),
                TotalTxGb = Math.Round(stats.BytesSent / 1024d / 1024d / 1024d, 2),
                PingMs = _cachedPingMs,
                PacketLossPercent = _cachedLossPercent,
                PublicIp = _cachedPublicIp ?? string.Empty,
                DnsServers = props.DnsAddresses.Select(a => a.ToString()).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect network info");
            return new Sample();
        }
    }

    private static NetworkInterface? PickActiveInterface()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        return nics.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g =>
                   g.Address.AddressFamily == AddressFamily.InterNetwork
                   && !IPAddress.IsLoopback(g.Address)))
               ?? nics.OrderByDescending(n => n.GetIPStatistics().BytesReceived).FirstOrDefault();
    }

    private async Task RefreshPingIfStaleAsync(bool wait)
    {
        if (DateTime.UtcNow - _pingAtUtc < PingCacheTtl && _cachedPingMs is not null)
        {
            return;
        }

        if (!wait)
        {
            if (!await _pingGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
        }
        else
        {
            await _pingGate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (DateTime.UtcNow - _pingAtUtc < PingCacheTtl && _cachedPingMs is not null)
            {
                return;
            }

            var (avg, loss) = await ProbeAsync().ConfigureAwait(false);
            _cachedPingMs = avg;
            _cachedLossPercent = loss;
            _pingAtUtc = DateTime.UtcNow;
        }
        finally
        {
            _pingGate.Release();
        }
    }

    private async Task RefreshPublicIpIfStaleAsync(bool wait)
    {
        if (DateTime.UtcNow - _publicIpAtUtc < PublicIpCacheTtl && !string.IsNullOrWhiteSpace(_cachedPublicIp))
        {
            return;
        }

        if (!wait)
        {
            if (!await _publicIpGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
        }
        else
        {
            await _publicIpGate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (DateTime.UtcNow - _publicIpAtUtc < PublicIpCacheTtl && !string.IsNullOrWhiteSpace(_cachedPublicIp))
            {
                return;
            }

            var ip = await LookupPublicIpAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                _cachedPublicIp = ip;
                _publicIpAtUtc = DateTime.UtcNow;
            }
        }
        finally
        {
            _publicIpGate.Release();
        }
    }

    private async Task<string?> LookupPublicIpAsync()
    {
        foreach (var url in PublicIpUrls)
        {
            try
            {
                using var response = await PublicIpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var text = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                if (IPAddress.TryParse(text, out var address)
                    && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                    && !IPAddress.IsLoopback(address))
                {
                    return address.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Public IP lookup failed from {Url}", url);
            }
        }

        return null;
    }

    private static async Task<(double? AvgMs, double LossPercent)> ProbeAsync()
    {
        var times = new List<long>(4);
        try
        {
            using var ping = new Ping();
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(PingTarget, 1000).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success)
                    {
                        times.Add(reply.RoundtripTime);
                    }
                }
                catch
                {
                    // Counted as loss.
                }
            }
        }
        catch
        {
            return (null, 100);
        }

        var loss = (4 - times.Count) / 4d * 100;
        return times.Count == 0 ? (null, loss) : (Math.Round(times.Average(), 0), loss);
    }

    private static NetworkInfo MapNetwork(Sample sample) => new()
    {
        ActiveInterface = string.IsNullOrWhiteSpace(sample.InterfaceName) ? "Unknown" : sample.InterfaceName,
        Status = string.IsNullOrWhiteSpace(sample.Status) ? "Disconnected" : sample.Status,
        IpAddress = sample.Ipv4,
        MacAddress = sample.MacAddress,
        Gateway = sample.Gateway,
        DnsServers = sample.DnsServers,
        DownloadMbps = sample.DownloadMbps,
        UploadMbps = sample.UploadMbps,
        TotalRxGB = sample.TotalRxGb,
        TotalTxGB = sample.TotalTxGb,
        PingMs = sample.PingMs,
        PacketLossPercent = sample.PacketLossPercent,
        PublicIp = sample.PublicIp
    };

    private static HardwareLevelDto MapLevel(Sample sample)
    {
        var items = new List<HardwareItemDto>
        {
            Item("Hostname", sample.Hostname),
            Item("Domain", sample.Domain),
            Item("Connection Type", sample.ConnectionType),
            Item("Status", sample.Status, sample.Status.Equals("Up", StringComparison.OrdinalIgnoreCase) ? "Green" : "Red"),
            Item("Interface Name", sample.InterfaceName),
            Item("Adapter Model", sample.AdapterModel),
            Item("MAC Address", sample.MacAddress),
            Item("Link Speed", sample.LinkSpeed),
            Item("Local IP", sample.Ipv4),
            Item("Public IP", sample.PublicIp),
            Item("IPv6 Address", sample.Ipv6),
            Item("Subnet Mask", sample.SubnetMask),
            Item("Default Gateway", sample.Gateway),
            Item("DNS Servers", sample.Dns),
            Item("DHCP Enabled", sample.DhcpEnabled, "Green"),
            Item("Download Speed", $"{sample.DownloadMbps:0.00} Mbps", sample.DownloadMbps > 0 ? "Green" : "Yellow"),
            Item("Upload Speed", $"{sample.UploadMbps:0.00} Mbps", sample.UploadMbps > 0 ? "Green" : "Yellow"),
            Item("Total RX", $"{sample.TotalRxGb:0.00} GB"),
            Item("Total TX", $"{sample.TotalTxGb:0.00} GB"),
            Item("Ping", sample.PingMs.HasValue ? $"{sample.PingMs:0} ms" : "-", sample.PingMs.HasValue ? "Green" : "Red"),
            Item("Packet Loss", sample.PacketLossPercent.HasValue ? $"{sample.PacketLossPercent:0}%" : "-", PacketLossStatus(sample.PacketLossPercent))
        };

        return new HardwareLevelDto
        {
            Level = 5,
            Title = "Network",
            Items = items,
            ItemCount = items.Count
        };
    }

    private static HardwareItemDto Item(string name, string? value, string? status = null)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return new HardwareItemDto
        {
            Name = name,
            Value = text,
            Status = status ?? (text == "-" ? "Red" : "Green")
        };
    }

    private static string PacketLossStatus(double? percent)
    {
        if (percent is null)
        {
            return "Red";
        }

        if (percent <= 0)
        {
            return "Green";
        }

        return percent < 25 ? "Yellow" : "Red";
    }

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length != 12)
        {
            return raw;
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    private static string FormatLinkSpeed(long speed)
    {
        if (speed <= 0)
        {
            return "-";
        }

        var mbps = speed / 1_000_000d;
        return mbps >= 1000 ? $"{mbps / 1000d:0.##} Gbps" : $"{mbps:0} Mbps";
    }

    private sealed class Sample
    {
        public string Hostname { get; init; } = Environment.MachineName;
        public string Domain { get; init; } = Environment.UserDomainName;
        public string ConnectionType { get; init; } = "-";
        public string Status { get; init; } = "Disconnected";
        public string InterfaceName { get; init; } = "-";
        public string AdapterModel { get; init; } = "-";
        public string MacAddress { get; init; } = "-";
        public string LinkSpeed { get; init; } = "-";
        public string Ipv4 { get; init; } = string.Empty;
        public string Ipv6 { get; init; } = string.Empty;
        public string SubnetMask { get; init; } = string.Empty;
        public string Gateway { get; init; } = string.Empty;
        public string Dns { get; init; } = string.Empty;
        public string DhcpEnabled { get; init; } = "-";
        public double DownloadMbps { get; init; }
        public double UploadMbps { get; init; }
        public double TotalRxGb { get; init; }
        public double TotalTxGb { get; init; }
        public double? PingMs { get; init; }
        public double? PacketLossPercent { get; init; }
        public string PublicIp { get; init; } = string.Empty;
        public string[] DnsServers { get; init; } = [];
    }
}
