using System.Net.Sockets;
using MonitorAgent.Service.Config;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Monitoring;

public sealed class WebsiteMonitor : BackgroundService, IMonitoringModule
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<WebsiteMonitor> _logger;
    private readonly Dictionary<string, string> _lastStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _nextCheckUtc = new(StringComparer.OrdinalIgnoreCase);

    public WebsiteMonitor(
        IHttpClientFactory httpClientFactory,
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        ILogger<WebsiteMonitor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("website");
        _configCache = configCache;
        _health = health;
        _logger = logger;
    }

    public string Name => nameof(WebsiteMonitor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Website monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var points = config.MonitorPoints
            .Where(p => p.Enabled && p.Type == MonitorPointType.Website)
            .ToList();

        var active = new HashSet<string>(points.Select(p => p.MonitorPointId), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _lastStatus.Keys.Where(id => !active.Contains(id)).ToList())
        {
            _health.ClearIssue(IssueKey(id));
            _lastStatus.Remove(id);
            _nextCheckUtc.Remove(id);
        }

        var now = DateTime.UtcNow;
        foreach (var point in points)
        {
            if (_nextCheckUtc.TryGetValue(point.MonitorPointId, out var due) && now < due)
            {
                continue;
            }

            var address = string.IsNullOrWhiteSpace(point.Address) ? "-" : point.Address.Trim();
            _logger.LogInformation("[Website] Checking {Name} ({Address})...", point.DisplayName, address);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var check = await CheckSchemesAsync(point.Address, cancellationToken);
            var interval = Math.Max(1, point.IntervalSeconds);
            _nextCheckUtc[point.MonitorPointId] = DateTime.UtcNow.AddSeconds(interval);

            if (check.Status == "Healthy")
            {
                _logger.LogInformation("[Website] {Name}: OK - {Message} ({Ms} ms, next check in {Interval}s)",
                    point.DisplayName, check.Message, watch.ElapsedMilliseconds, interval);
            }
            else
            {
                _logger.LogWarning("[Website] {Name}: {Status} - {Reason} ({Ms} ms, next check in {Interval}s)",
                    point.DisplayName, check.Status.ToUpperInvariant(), check.AlertReason, watch.ElapsedMilliseconds, interval);
            }

            if (check.Status == "Healthy")
            {
                _health.SetPointHealth(point.MonitorPointId, true, check.Message, "Healthy", watch.ElapsedMilliseconds);
                _health.ClearIssue(IssueKey(point.MonitorPointId));
            }
            else if (check.Status == "Warning")
            {
                _health.SetPointHealth(point.MonitorPointId, true, check.AlertReason, "Warning", watch.ElapsedMilliseconds);
                _health.SetIssue(
                    IssueKey(point.MonitorPointId),
                    "Warning",
                    check.Title,
                    IssueText.WebsiteHttpsOnly(point.DisplayName, address, check.AlertReason),
                    point.MonitorPointId);
            }
            else
            {
                _health.SetPointHealth(point.MonitorPointId, false, check.AlertReason, "Critical");
                _health.SetIssue(
                    IssueKey(point.MonitorPointId),
                    "Critical",
                    check.Title,
                    IssueText.WebsiteDown(point.DisplayName, address, check.AlertReason),
                    point.MonitorPointId);
            }

            _lastStatus[point.MonitorPointId] = check.Status;
        }
    }

    private async Task<ProbeResult> CheckSchemesAsync(string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return ProbeResult.Fail(
                "Address is missing",
                "Address is missing",
                "The address is empty. Enter the website or API address in the Address field.");
        }

        if (!TryNormalize(address, out var uri))
        {
            return ProbeResult.Fail(
                "Address is not a valid URL",
                "Invalid address",
                "The address is not valid. Enter a link such as https://example.com or example.com.");
        }

        if (address.Contains("://", StringComparison.Ordinal))
        {
            return await ProbeUriAsync(uri, cancellationToken);
        }

        var httpTask = ProbeUriAsync(WithScheme(uri, Uri.UriSchemeHttp), cancellationToken);
        var httpsTask = ProbeUriAsync(WithScheme(uri, Uri.UriSchemeHttps), cancellationToken);
        await Task.WhenAll(httpTask, httpsTask);
        var http = await httpTask;
        var https = await httpsTask;

        if (https.Up)
        {
            return ProbeResult.Ok(https.Message);
        }

        if (http.Up)
        {
            return new ProbeResult(false, http.Message, "HTTPS not available", $"Works over HTTP only; HTTPS failed. {https.AlertReason}") { Status = "Warning" };
        }

        return uri.Scheme == Uri.UriSchemeHttps ? https : http;
    }

    private async Task<ProbeResult> ProbeUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return ProbeResult.Ok($"Website is reachable (HTTP {(int)response.StatusCode})");
            }

            return ExplainStatus(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.Fail(
                "Website did not respond",
                "Website not responding",
                $"The website did not respond within {RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return ClassifyTransport(ex);
        }
    }

    private static ProbeResult ExplainStatus(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        var reason = response.ReasonPhrase;
        var label = string.IsNullOrWhiteSpace(reason) ? code.ToString() : $"{code} {reason}";
        var meaning = code switch
        {
            400 => "The request was not understood. Check the API address and path.",
            401 => "The website requires a login or access key, and the request was rejected.",
            403 => "Access to this address is forbidden.",
            404 => "The website or page does not exist at this address.",
            408 => "The server closed the request because it waited too long.",
            429 => "The website rejected the request because of too many attempts.",
            500 => "The server had an internal error while handling the request.",
            502 => "The gateway could not reach the server behind it.",
            503 => "The service is currently unavailable.",
            504 => "The gateway waited for the server but got no reply.",
            >= 500 => "The server returned an error and the request did not complete.",
            _ => "The request failed and the website returned an error."
        };

        return ProbeResult.Fail(
            $"Website returned HTTP {label}",
            code == 404 ? "Website not found" : "Website returned an error",
            $"The website returned HTTP {label}. {meaning}");
    }

    private static ProbeResult ClassifyTransport(HttpRequestException ex)
    {
        var socket = FindSocket(ex);
        if (ex.HttpRequestError == HttpRequestError.NameResolutionError
            || socket?.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return ProbeResult.Fail(
                "Website host was not found",
                "Website not found",
                "The domain name was not found. Check the address spelling and that the domain is registered.");
        }

        if (socket?.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return ProbeResult.Fail(
                "Connection refused",
                "Connection failed",
                "The server refused the connection. The service may be stopped or the port may be closed.");
        }

        if (socket?.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.HostUnreachable)
        {
            return ProbeResult.Fail(
                "Network unreachable",
                "Unreachable",
                "The network cannot reach this address.");
        }

        if (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            var cause = FirstLine(ex.InnerException?.Message ?? ex.Message);
            return ProbeResult.Fail(
                "Secure connection failed",
                "Secure connection failed",
                string.IsNullOrWhiteSpace(cause)
                    ? "The secure (HTTPS) connection failed. The website certificate is not trusted or the TLS version is not supported."
                    : $"The secure (HTTPS) connection failed. {cause}");
        }

        if (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return ProbeResult.Fail(
                "Website is unreachable",
                "Connection failed",
                "The connection dropped or the address is not accepting requests.");
        }

        var detail = FirstLine(ex.InnerException?.Message ?? ex.Message);
        return ProbeResult.Fail(
            string.IsNullOrWhiteSpace(detail) ? "Website is unreachable" : detail,
            "Connection failed",
            string.IsNullOrWhiteSpace(detail)
                ? "Could not connect to the website."
                : $"Could not connect to the website. {detail}");
    }

    private static SocketException? FindSocket(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket;
            }
        }

        return null;
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        return line.Length > 180 ? line[..180] : line;
    }

    private static Uri WithScheme(Uri uri, string scheme)
    {
        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        if (!uri.IsDefaultPort &&
            ((scheme == Uri.UriSchemeHttps && uri.Port == 80) || (scheme == Uri.UriSchemeHttp && uri.Port == 443)))
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }

    private static bool TryNormalize(string address, out Uri uri)
    {
        var value = address.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string IssueKey(string monitorPointId) => $"website:{monitorPointId}";

    private readonly record struct ProbeResult(bool Up, string Message, string Title, string AlertReason)
    {
        public string Status { get; init; } = "Critical";

        public static ProbeResult Ok(string message) => new(true, message, string.Empty, string.Empty) { Status = "Healthy" };

        public static ProbeResult Fail(string message, string title, string alertReason) => new(false, message, title, alertReason);
    }
}
