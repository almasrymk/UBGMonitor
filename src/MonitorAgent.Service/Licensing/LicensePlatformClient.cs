using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Runtime;

namespace MonitorAgent.Service.Licensing;

public enum LicenseReplyKind
{
    Accepted,
    /// <summary>The server looked at the license and said no; the reason is in the error code.</summary>
    Rejected,
    /// <summary>No decision: no internet, server down, rate limited or not allowed to ask. The license itself is not at fault.</summary>
    Unavailable
}

public sealed record LicenseReply(
    LicenseReplyKind Kind,
    string Message,
    string? ErrorCode = null,
    string? Token = null,
    int? NextCheckAfterSeconds = null,
    string? LicenseNumber = null,
    TimeSpan? RetryAfter = null);

public interface ILicensePlatformClient
{
    Task<LicenseReply> ActivateAsync(string productKey, string deviceId, CancellationToken cancellationToken);

    Task<LicenseReply> CheckAsync(string productKey, string deviceId, bool heartbeat, CancellationToken cancellationToken);

    Task<LicenseReply> DeactivateAsync(string productKey, string deviceId, CancellationToken cancellationToken);

    /// <returns>The JWK list as JSON, or null when it could not be downloaded.</returns>
    Task<string?> GetSigningKeysAsync(CancellationToken cancellationToken);
}

/// <summary>Calls the licensing site's device endpoints using installation or legacy administrator-supplied credentials.</summary>
public sealed class LicensePlatformClient : ILicensePlatformClient
{
    public const string HttpClientName = "licensing";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAgentIdentity _identity;
    private readonly LicensingOptions _options;
    private readonly ILogger<LicensePlatformClient> _logger;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTime _renewTokenAtUtc;

    public LicensePlatformClient(
        IHttpClientFactory httpClientFactory,
        IAgentIdentity identity,
        IOptions<LicensingOptions> options,
        ILogger<LicensePlatformClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identity = identity;
        _options = options.Value;
        _logger = logger;
    }

    public Task<LicenseReply> ActivateAsync(string productKey, string deviceId, CancellationToken cancellationToken)
        => PostAsync("activate", new
        {
            productKey,
            deviceId,
            deviceName = Environment.MachineName,
            productCode = _options.ProductCode,
            appVersion = _identity.Version,
            os = OsName
        }, Guid.NewGuid().ToString(), deviceId, cancellationToken);

    public Task<LicenseReply> CheckAsync(string productKey, string deviceId, bool heartbeat, CancellationToken cancellationToken)
        => PostAsync(heartbeat ? "heartbeat" : "validate", new
        {
            productKey,
            deviceId,
            productCode = _options.ProductCode,
            appVersion = _identity.Version,
            os = OsName
        }, null, deviceId, cancellationToken);

    public Task<LicenseReply> DeactivateAsync(string productKey, string deviceId, CancellationToken cancellationToken)
        => PostAsync("deactivate", new { productKey, deviceId }, null, deviceId, cancellationToken);

    public async Task<string?> GetSigningKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client().GetAsync(new Uri(BaseUri, "api/v1/signing-keys"), cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[License] Could not download the signing keys: {Message}", ex.Message);
            return null;
        }
    }

    private static string OsName => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    private Uri BaseUri => new(_options.PlatformUrl.TrimEnd('/') + "/");

    private string ClientId => _options.DeviceCredential?.ClientId ?? _options.ClientId;
    private string ClientSecret => _options.DeviceCredential?.ClientSecret ?? _options.ClientSecret;

    private async Task<LicenseReply> PostAsync(string action, object body, string? idempotencyKey, string deviceId, CancellationToken cancellationToken)
    {
        if (_options.DeviceCredential is { } enrolled && !enrolled.Matches(deviceId, _options))
            return new LicenseReply(LicenseReplyKind.Unavailable, "The enrollment credential does not match this installation.", "DEVICE_ENROLLMENT_INVALID");
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            return new LicenseReply(LicenseReplyKind.Unavailable, "An installation credential is required.", "DEVICE_CREDENTIAL_REQUIRED");
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var token = await GetAccessTokenAsync(cancellationToken);
                if (token is null)
                    return new LicenseReply(LicenseReplyKind.Unavailable, "The platform could not authenticate this installation.", "DEVICE_AUTH_UNAVAILABLE");
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, $"api/v1/licensing/{action}"))
                {
                    Content = JsonContent.Create(body)
                };
                if (token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                if (idempotencyKey is not null)
                {
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
                }

                using var response = await Client().SendAsync(request, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null && attempt == 0)
                {
                    Interlocked.CompareExchange(ref _accessToken, null, token);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                return Interpret(response.StatusCode, text, response.Headers.RetryAfter?.Delta);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new LicenseReply(LicenseReplyKind.Unavailable, $"The license server could not be reached: {ex.Message}");
        }
    }

    /// <summary>A client-credentials token from the licensing site, reused until shortly before it expires.</summary>
    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _accessToken) is { } current && DateTime.UtcNow < _renewTokenAtUtc)
        {
            return current;
        }

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is { } renewed && DateTime.UtcNow < _renewTokenAtUtc)
            {
                return renewed;
            }

            using var response = await Client().PostAsJsonAsync(
                new Uri(BaseUri, "api/v1/auth/client-token"),
                new { clientId = ClientId, clientSecret = ClientSecret },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[License] The licensing site refused the built-in API client (HTTP {Status})", (int)response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var token = Text(root, "accessToken", "access_token", "token");
            if (token is null)
            {
                return null;
            }

            var lifetime = root.TryGetProperty("expiresIn", out var e) || root.TryGetProperty("expires_in", out e)
                ? TimeSpan.FromSeconds(e.GetDouble())
                : TimeSpan.FromMinutes(10);
            _accessToken = token;
            _renewTokenAtUtc = DateTime.UtcNow + (lifetime > RenewBefore * 2 ? lifetime - RenewBefore : lifetime / 2);
            return token;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Turns the server's answer into accepted, rejected or no decision.</summary>
    public static LicenseReply Interpret(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        JsonElement root = default;
        var hasJson = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                root = document.RootElement.Clone();
                hasJson = root.ValueKind == JsonValueKind.Object;
            }
        }
        catch (JsonException)
        {
        }

        var code = hasJson ? Text(root, "code", "errorCode", "error") : null;
        var message = (hasJson ? Text(root, "detail", "title", "message") : null) ?? $"HTTP {(int)status}";

        if ((int)status is >= 200 and < 300)
        {
            if (!hasJson)
            {
                return new LicenseReply(LicenseReplyKind.Accepted, "Accepted.");
            }

            var token = Text(root, "signedLicenseToken", "licenseToken", "signedToken", "token");
            var next = root.TryGetProperty("nextCheckAfterSeconds", out var n) && n.TryGetInt32(out var seconds) ? seconds : (int?)null;
            var licenseNumber = Text(root, "licenseNumber");
            if (root.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.False)
            {
                var statusText = Text(root, "status", "reason");
                return new LicenseReply(LicenseReplyKind.Rejected, Text(root, "message", "reason") ?? $"The license is {statusText ?? "not valid"}.",
                    code ?? CodeForStatus(statusText), LicenseNumber: licenseNumber);
            }

            return new LicenseReply(LicenseReplyKind.Accepted, "Accepted.", null, token, next, licenseNumber);
        }

        var isDecision = code?.StartsWith("LIC_", StringComparison.OrdinalIgnoreCase) == true
            || (status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Gone or HttpStatusCode.UnprocessableEntity
                && code?.StartsWith("AUTH_", StringComparison.OrdinalIgnoreCase) != true
                && code?.Equals("RATE_LIMITED", StringComparison.OrdinalIgnoreCase) != true);
        if (isDecision)
        {
            return new LicenseReply(LicenseReplyKind.Rejected, message, code ?? (status == HttpStatusCode.NotFound ? "LIC_INVALID_LICENSE" : null));
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new LicenseReply(LicenseReplyKind.Unavailable,
                $"The licensing site did not accept this copy of MonitorAgent ({code ?? $"HTTP {(int)status}"}). Install the latest version or contact your provider.",
                code, RetryAfter: retryAfter);
        }

        if ((int)status >= 500)
        {
            var reference = hasJson ? Text(root, "traceId") : null;
            return new LicenseReply(LicenseReplyKind.Unavailable,
                $"The licensing site had an internal error, so the license could not be checked. Try again later or contact your provider{(reference is null ? "" : $" (reference {reference})")}.",
                code, RetryAfter: retryAfter);
        }

        return new LicenseReply(LicenseReplyKind.Unavailable, $"The license server did not decide ({(int)status} {code ?? message}).", code, RetryAfter: retryAfter);
    }

    private static string? CodeForStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "expired" => "LIC_LICENSE_EXPIRED",
        "suspended" => "LIC_LICENSE_SUSPENDED",
        "revoked" => "LIC_LICENSE_REVOKED",
        _ => null
    };

    private static string? Text(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }

        return null;
    }

    private HttpClient Client()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
        return client;
    }
}
