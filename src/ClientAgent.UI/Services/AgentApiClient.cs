using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Models;

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

    public async Task<TopProcessesQueryResult> GetTopProcessesAsync(ProcessSortBy sortBy, int count = 5, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        var sort = sortBy switch
        {
            ProcessSortBy.Ram => "ram",
            ProcessSortBy.Network => "network",
            _ => "cpu"
        };
        var route = $"{ApiRoutes.ProcessesTop}?count={count}&sortBy={sort}";

        try
        {
            Debug.WriteLine($"[API] Calling {route}...");
            var response = await _http.GetAsync(route, linked.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Debug.WriteLine($"[API] processes 404: {route}");
                return TopProcessesQueryResult.Fail("Endpoint not found (404)");
            }

            if (response.StatusCode == HttpStatusCode.NotImplemented)
            {
                Debug.WriteLine($"[API] processes 501: {route}");
                return TopProcessesQueryResult.Fail("Not implemented (501)");
            }

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[API] processes HTTP {(int)response.StatusCode}");
                return TopProcessesQueryResult.Fail($"Error: HTTP {(int)response.StatusCode}");
            }

            var dtos = await response.Content.ReadFromJsonAsync<List<ProcessTopDto>>(JsonOptions, linked.Token) ?? [];
            var max = dtos.Count == 0 ? 0d : dtos.Max(d => d.Value);
            var items = dtos.Select((dto, index) => new ProcessItem
            {
                Rank = index + 1,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "-" : dto.Name,
                Pid = dto.Pid,
                Value = dto.Value,
                Unit = dto.Unit,
                Percent = sortBy == ProcessSortBy.Cpu
                    ? Math.Clamp(dto.Value, 0, 100)
                    : max <= 0 ? 0 : dto.Value / max * 100,
                DisplayValue = FormatDisplayValue(dto.Value, dto.Unit),
                Icon = ProcessIconCache.Get(dto.Pid, dto.Name)
            }).ToList();
            return TopProcessesQueryResult.Ok(items);
        }
        catch (TaskCanceledException ex)
        {
            Debug.WriteLine($"[API] Timeout (processes){Environment.NewLine}{ex}");
            return TopProcessesQueryResult.Fail("Timeout");
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[API] HTTP Error (processes){Environment.NewLine}{ex}");
            if (ex.StatusCode is null)
            {
                return TopProcessesQueryResult.Fail("Service not running");
            }

            return TopProcessesQueryResult.Fail($"Error: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Debug.WriteLine($"[API] Socket Error (processes){Environment.NewLine}{ex}");
            return TopProcessesQueryResult.Fail("Service not running");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[API] Unexpected Error (processes){Environment.NewLine}{ex}");
            return TopProcessesQueryResult.Fail($"Error: {ex.Message}");
        }
    }

    private static string FormatDisplayValue(double value, string unit)
    {
        if (string.Equals(unit, "%", StringComparison.Ordinal))
        {
            return $"{value:0}%";
        }

        if (string.Equals(unit, "MB", StringComparison.OrdinalIgnoreCase) && value >= 1024)
        {
            return $"{value / 1024:0.0} GB";
        }

        if (string.Equals(unit, "KB/s", StringComparison.OrdinalIgnoreCase) && value >= 1024)
        {
            return $"{value / 1024:0.0} MB/s";
        }

        if (string.Equals(unit, "KB/s", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:0.0} KB/s";
        }

        return string.IsNullOrWhiteSpace(unit)
            ? $"{value:0}"
            : $"{value:0} {unit}";
    }

    public Task<HardwareInfo?> GetHardwareAsync(CancellationToken ct = default)
        => GetAsync<HardwareInfo>(ApiRoutes.Hardware, "hardware", ct);

    public Task<HardwareResponseDto?> GetHardwareLevelsAsync(CancellationToken ct = default)
        => GetAsync<HardwareResponseDto>(ApiRoutes.HardwareLevels, "hardware-levels", ct);

    public Task<HardwareLevelDto?> GetHardwareLevelAsync(int level, CancellationToken ct = default)
        => GetAsync<HardwareLevelDto>($"{ApiRoutes.HardwareLevels}/{level}", $"hardware-level-{level}", ct);

    public Task<OsInfo?> GetOsAsync(CancellationToken ct = default)
        => GetAsync<OsInfo>(ApiRoutes.Os, "os", ct);

    public Task<SensorsInfo?> GetSensorsAsync(CancellationToken ct = default)
        => GetAsync<SensorsInfo>(ApiRoutes.Sensors, "sensors", ct);

    public Task<List<MonitorPointStatusDto>?> GetMonitorPointsAsync(CancellationToken ct = default)
        => GetAsync<List<MonitorPointStatusDto>>(ApiRoutes.MonitorPoints, "monitorpoints", ct);

    public Task<List<AgentIssueDto>?> GetIssuesAsync(CancellationToken ct = default)
        => GetAsync<List<AgentIssueDto>>(ApiRoutes.Issues, "issues", ct);

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
