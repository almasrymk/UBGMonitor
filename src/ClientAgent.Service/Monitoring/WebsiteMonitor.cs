using System.Net.Sockets;
using ClientAgent.Service.Config;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Monitoring;

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

            var check = await CheckSchemesAsync(point.Address, cancellationToken);
            var interval = Math.Max(1, point.IntervalSeconds);
            _nextCheckUtc[point.MonitorPointId] = DateTime.UtcNow.AddSeconds(interval);

            _lastStatus.TryGetValue(point.MonitorPointId, out var previous);
            var address = string.IsNullOrWhiteSpace(point.Address) ? "-" : point.Address.Trim();
            if (check.Status == "Healthy")
            {
                _health.SetPointHealth(point.MonitorPointId, true, check.Message, "Healthy");
                _health.ClearIssue(IssueKey(point.MonitorPointId));
            }
            else if (check.Status == "Warning")
            {
                _health.SetPointHealth(point.MonitorPointId, true, check.AlertReason, "Warning");
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

            if (previous is "Healthy" or "Warning" && check.Status == "Critical")
            {
                _logger.LogWarning("Website {Name} ({Address}) failed: {Message}", point.DisplayName, point.Address, check.Message);
            }
            else if (previous != "Warning" && check.Status == "Warning")
            {
                _logger.LogWarning("Website {Name} ({Address}) works on HTTP only", point.DisplayName, point.Address);
            }
            else if (previous is "Critical" or "Warning" && check.Status == "Healthy")
            {
                _logger.LogInformation("Website {Name} ({Address}) recovered", point.DisplayName, point.Address);
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
                "عنوان ناقص",
                "العنوان فارغ. أضف عنوان الموقع أو واجهة الـ API في حقل Address.");
        }

        if (!TryNormalize(address, out var uri))
        {
            return ProbeResult.Fail(
                "Address is not a valid URL",
                "عنوان غير صالح",
                "العنوان غير صالح. اكتب رابطاً مثل https://example.com أو example.com.");
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
            return new ProbeResult(false, http.Message, "HTTPS غير متاح", https.AlertReason) { Status = "Warning" };
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
                "الموقع لا يستجيب",
                $"الموقع لا يستجيب. انتهت مهلة الانتظار ({RequestTimeout.TotalSeconds:0} ثوانٍ) ولم يصل أي رد.");
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
            400 => "الطلب غير مفهوم. راجع عنوان الـ API والمسار.",
            401 => "الموقع يطلب تسجيل دخول أو مفتاح وصول، والطلب رُفض.",
            403 => "الوصول إلى هذا العنوان مرفوض.",
            404 => "الموقع أو الصفحة غير موجودة على هذا العنوان.",
            408 => "الخادم أغلق الطلب لأن الانتظار طال.",
            429 => "الموقع رفض الطلب لكثرة المحاولات.",
            500 => "الخادم واجه خطأ داخلياً أثناء معالجة الطلب.",
            502 => "البوابة لم تصل إلى الخادم الذي خلفها.",
            503 => "الخدمة غير متاحة حالياً.",
            504 => "البوابة انتظرت الخادم ولم يصلها رد.",
            >= 500 => "الخادم رجع خطأ ولم يكتمل الطلب.",
            _ => "الطلب لم ينجح والموقع رجع خطأ."
        };

        return ProbeResult.Fail(
            $"Website returned HTTP {label}",
            code == 404 ? "الموقع غير موجود" : "الموقع رجع خطأ",
            $"الموقع رجع خطأ HTTP {label}. {meaning}");
    }

    private static ProbeResult ClassifyTransport(HttpRequestException ex)
    {
        var socket = FindSocket(ex);
        if (ex.HttpRequestError == HttpRequestError.NameResolutionError
            || socket?.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return ProbeResult.Fail(
                "Website host was not found",
                "الموقع غير موجود",
                "لم يتم العثور على الموقع. اسم النطاق غير موجود، تأكد من كتابة العنوان ومن أن النطاق مسجل.");
        }

        if (socket?.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return ProbeResult.Fail(
                "Connection refused",
                "تعذر الاتصال",
                "تعذر فتح الاتصال. الجهاز رفض الاتصال، وقد تكون الخدمة متوقفة أو المنفذ مغلقاً.");
        }

        if (socket?.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.HostUnreachable)
        {
            return ProbeResult.Fail(
                "Network unreachable",
                "تعذر الوصول",
                "تعذر الوصول إلى الموقع. الشبكة لا تصل إلى هذا العنوان.");
        }

        if (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            var cause = FirstLine(ex.InnerException?.Message ?? ex.Message);
            return ProbeResult.Fail(
                "Secure connection failed",
                "فشل الاتصال الآمن",
                string.IsNullOrWhiteSpace(cause)
                    ? "فشل الاتصال الآمن (HTTPS). شهادة الموقع غير موثوقة أو بروتوكول TLS غير مدعوم."
                    : $"فشل الاتصال الآمن (HTTPS). {cause}");
        }

        if (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return ProbeResult.Fail(
                "Website is unreachable",
                "تعذر الاتصال",
                "تعذر الوصول إلى الموقع. الاتصال انقطع أو العنوان لا يستقبل الطلبات.");
        }

        var detail = FirstLine(ex.InnerException?.Message ?? ex.Message);
        return ProbeResult.Fail(
            string.IsNullOrWhiteSpace(detail) ? "Website is unreachable" : detail,
            "تعذر الاتصال",
            string.IsNullOrWhiteSpace(detail)
                ? "تعذر الاتصال بالموقع."
                : $"تعذر الاتصال بالموقع. {detail}");
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
