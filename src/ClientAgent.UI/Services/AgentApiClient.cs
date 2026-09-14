using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.Services;

public sealed class AgentApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;

    public AgentApiClient()
    {
        _http = new HttpClient(CreateIpv4Handler(), disposeHandler: true)
        {
            // Use 127.0.0.1 instead of localhost to avoid ::1 (IPv6) on Windows.
            BaseAddress = new Uri("http://127.0.0.1:5050"),
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public AgentApiClient(HttpClient httpClient)
    {
        _http = httpClient;
        _http.BaseAddress ??= new Uri("http://127.0.0.1:5050");
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout < TimeSpan.FromSeconds(5))
        {
            _http.Timeout = TimeSpan.FromSeconds(20);
        }
    }

    public Task<AgentStatusDto?> GetStatusAsync(CancellationToken ct = default)
        => GetAsync<AgentStatusDto>(ApiRoutes.Status, "status", ct);

    public Task<SystemSnapshot?> GetSnapshotAsync(CancellationToken ct = default)
        => GetAsync<SystemSnapshot>(ApiRoutes.Snapshot, "snapshot", ct);

    public Task<CpuInfo?> GetCpuAsync(CancellationToken ct = default)
        => GetAsync<CpuInfo>(ApiRoutes.Cpu, "cpu", ct);

    public Task<RamInfo?> GetRamAsync(CancellationToken ct = default)
        => GetAsync<RamInfo>(ApiRoutes.Ram, "ram", ct);

    public Task<List<DiskPartition>?> GetPartitionsAsync(CancellationToken ct = default)
        => GetAsync<List<DiskPartition>>(ApiRoutes.DiskPartitions, "partitions", ct);

    public Task<NetworkInfo?> GetNetworkAsync(CancellationToken ct = default)
        => GetAsync<NetworkInfo>(ApiRoutes.Network, "network", ct);

    public Task<List<ProcessInfo>?> GetTopProcessesAsync(int count = 5, CancellationToken ct = default)
        => GetAsync<List<ProcessInfo>>($"{ApiRoutes.ProcessesTop}?count={count}", "processes", ct);

    public Task<HardwareInfo?> GetHardwareAsync(CancellationToken ct = default)
        => GetAsync<HardwareInfo>(ApiRoutes.Hardware, "hardware", ct);

    public Task<OsInfo?> GetOsAsync(CancellationToken ct = default)
        => GetAsync<OsInfo>(ApiRoutes.Os, "os", ct);

    public Task<List<MonitorPoint>?> GetMonitorPointsAsync(CancellationToken ct = default)
        => GetAsync<List<MonitorPoint>>(ApiRoutes.MonitorPoints, "monitorpoints", ct);

    public Task<List<MonitoringEvent>?> GetRecentEventsAsync(int count, CancellationToken ct = default)
        => GetAsync<List<MonitoringEvent>>($"{ApiRoutes.EventsRecent}?count={count}", "events", ct);

    private async Task<T?> GetAsync<T>(string route, string name, CancellationToken ct)
        where T : class
    {
        try
        {
            Debug.WriteLine($"[API] Calling {route}...");
            var result = await _http.GetFromJsonAsync<T>(route, JsonOptions, ct);
            Debug.WriteLine($"[API] {name} OK");
            return result;
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[API] HTTP Error ({name}): {ex.Message}");
            Debug.WriteLine($"[API] Status Code: {ex.StatusCode}");
            return null;
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine($"[API] Timeout ({name})");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] Unexpected Error ({name}): {ex.Message}");
            return null;
        }
    }

    private static SocketsHttpHandler CreateIpv4Handler()
    {
        return new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, context.DnsEndPoint.Port);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }
}
