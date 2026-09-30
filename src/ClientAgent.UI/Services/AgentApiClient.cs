using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Models.Reports;
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

    private readonly bool _ownsHttpClient;
    private HttpClient _http;

    public AgentApiClient()
    {
        _ownsHttpClient = true;
        _http = CreateClient(new Uri("http://127.0.0.1:5050"));
    }

    /// <summary>The service address as typed ("192.168.1.10", "pc-name:5050" or a full URL); null when it is not valid.</summary>
    public static Uri? ParseAddress(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return uri.IsDefaultPort && !text!.Contains($":{uri.Port}", StringComparison.Ordinal)
            ? new UriBuilder(uri) { Port = 5050 }.Uri
            : uri;
    }

    private string _accessKey = string.Empty;

    /// <summary>True when the service is on this computer, so secrets can be encrypted here for it.</summary>
    public bool IsLocal => _http.BaseAddress is not { } address
        || address.IsLoopback
        || address.Host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    public void SetBaseAddress(string? url, string? accessKey = null)
    {
        if (ParseAddress(url) is not { } uri)
        {
            return;
        }

        var key = accessKey?.Trim() ?? string.Empty;
        if (_http.BaseAddress is not null && SameRequestUri(_http.BaseAddress, uri) && key == _accessKey)
        {
            return;
        }

        if (!_ownsHttpClient)
        {
            return;
        }

        _accessKey = key;
        var replacement = CreateClient(uri);
        if (key.Length > 0)
        {
            replacement.DefaultRequestHeaders.Add(ApiRoutes.AccessKeyHeader, key);
        }

        var previous = _http;
        _http = replacement;
        previous.Dispose();
    }

    public AgentApiClient(HttpClient httpClient)
    {
        _ownsHttpClient = false;
        _http = httpClient;
        _http.BaseAddress ??= new Uri("http://127.0.0.1:5050");
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout < TimeSpan.FromSeconds(5))
        {
            _http.Timeout = TimeSpan.FromSeconds(20);
        }
    }

    /// <summary>Warnings and problems built by the service (the app only displays them).</summary>
    public Task<List<AgentIssueDto>?> GetNotificationsAsync(CancellationToken ct = default)
        => GetAsync<List<AgentIssueDto>>(ApiRoutes.Notifications, "notifications", ct);

    public Task<InternetStateDto?> GetInternetAsync(CancellationToken ct = default)
        => GetAsync<InternetStateDto>(ApiRoutes.Internet, "internet", ct);

    public async Task<InternetStateDto?> StartSpeedTestAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsync(ApiRoutes.InternetSpeedTest, null, ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<InternetStateDto>(JsonOptions, ct)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Debug.WriteLine($"[AgentApiClient] speed test start failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The "Setting" section of the service's appsettings.json.</summary>
    public Task<System.Text.Json.Nodes.JsonObject?> GetSettingsAsync(CancellationToken ct = default)
        => GetAsync<System.Text.Json.Nodes.JsonObject>(ApiRoutes.Settings, "settings", ct);

    /// <summary>Asks the service to save its settings; returns null on success, otherwise why it failed.</summary>
    public async Task<string?> SaveSettingsAsync(System.Text.Json.Nodes.JsonObject section, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PutAsync(ApiRoutes.Settings, JsonContent.Create(section), ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var detail = string.Empty;
            try
            {
                detail = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["detail"]?.GetValue<string>() ?? string.Empty;
            }
            catch (JsonException)
            {
            }

            return string.IsNullOrWhiteSpace(detail)
                ? $"The Agent service could not save the settings (HTTP {(int)response.StatusCode})."
                : detail;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "The Agent service is not running, so the settings could not be saved.";
        }
    }

    public Task<AgentStatusDto?> GetStatusAsync(CancellationToken ct = default)
        => GetAsync<AgentStatusDto>(ApiRoutes.Status, "status", ct);

    /// <summary>Why the service at the current address does not answer: null when it does.</summary>
    public async Task<string?> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(ApiRoutes.Status, ct);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The service asks for an access key, and the one in this app is missing or wrong.",
                _ when response.IsSuccessStatusCode => null,
                _ => $"The service answered with HTTP {(int)response.StatusCode}."
            };
        }
        catch (HttpRequestException ex)
        {
            return $"No Agent service answers at {_http.BaseAddress}: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return $"No Agent service answered at {_http.BaseAddress} in time.";
        }
    }

    /// <summary>Tries the connection from the service's computer, the same way the monitor point will.</summary>
    public async Task<DatabaseTestResultDto> TestDatabaseAsync(DatabaseLogin login, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(ApiRoutes.DatabaseTest, login, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DatabaseTestResultDto(false, "The Agent service is too old for this test; reinstall it.");
            }

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<DatabaseTestResultDto>(JsonOptions, ct) ?? new DatabaseTestResultDto(false, "No answer.")
                : new DatabaseTestResultDto(false, $"The Agent service answered with HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new DatabaseTestResultDto(false, "The Agent service is not answering, so the connection could not be tested.");
        }
    }

    public Task<SystemSnapshot?> GetSnapshotAsync(CancellationToken ct = default)
        => GetAsync<SystemSnapshot>(ApiRoutes.Snapshot, "snapshot", ct);

    public Task<CpuInfo?> GetCpuAsync(CancellationToken ct = default)
        => GetAsync<CpuInfo>(ApiRoutes.Cpu, "cpu", ct);

    public Task<RamInfo?> GetRamAsync(CancellationToken ct = default)
        => GetAsync<RamInfo>(ApiRoutes.Ram, "ram", ct);

    public Task<List<DiskPartition>?> GetPartitionsAsync(CancellationToken ct = default)
        => GetAsync<List<DiskPartition>>(ApiRoutes.DiskPartitions, "partitions", ct);

    public Task<List<PhysicalDisk>?> GetPhysicalDisksAsync(CancellationToken ct = default)
        => GetAsync<List<PhysicalDisk>>(ApiRoutes.DiskPhysical, "physical-disks", ct);

    public Task<DiskActivityDto?> GetDiskActivityAsync(CancellationToken ct = default)
        => GetAsync<DiskActivityDto>(ApiRoutes.DiskActivity, "disk-activity", ct);

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
            ProcessSortBy.Disk => "disk",
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

    /// <param name="from">Local time.</param>
    /// <param name="to">Local time.</param>
    public Task<ReportDto?> GetReportAsync(string type, DateTime from, DateTime to, string? subject = null, CancellationToken ct = default)
        => GetAsync<ReportDto>(
            $"{ApiRoutes.Reports}/{Uri.EscapeDataString(type)}?from={from:yyyy-MM-ddTHH:mm:ss}&to={to:yyyy-MM-ddTHH:mm:ss}"
                + (string.IsNullOrEmpty(subject) ? string.Empty : $"&subject={Uri.EscapeDataString(subject)}"),
            $"report-{type}", ct);

    public Task<List<ReportSubject>?> GetReportSubjectsAsync(CancellationToken ct = default)
        => GetAsync<List<ReportSubject>>(ApiRoutes.ReportSubjects, "report-subjects", ct);

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

    private static HttpClient CreateClient(Uri baseAddress)
        => new(CreateIpv4Handler(), disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(20)
        };

    private static bool SameRequestUri(Uri left, Uri right)
        => Uri.Compare(left, right, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>IPv4 only: "localhost" is 127.0.0.1 (the service does not listen on ::1), a computer name is looked up.</summary>
    private static async Task<IPAddress> ResolveConnectAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            return parsed;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        return addresses.FirstOrDefault() ?? throw new HttpRequestException($"The computer \"{host}\" was not found.");
    }

    private static SocketsHttpHandler CreateIpv4Handler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var address = await ResolveConnectAddressAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
                var endpoint = new IPEndPoint(address, context.DnsEndPoint.Port);
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
