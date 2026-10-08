using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorAgent.Cloud;

public sealed record EnrollmentResult(
    Guid DeviceId, string DeviceSecret, string GatewayUrl, string? TenantName, string? LocationName, string? LicenseState, string? LicenseToken, DateTimeOffset? LicenseCheckAfter);

public sealed record DeviceToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>A refusal of the cloud with its problem <c>code</c> (05 section 1); <see cref="RetryAfter"/> is set for 429.</summary>
public sealed class CloudRequestException(HttpStatusCode status, string code, string? detail, TimeSpan? retryAfter = null)
    : Exception($"{(int)status} {code}{(detail is null ? string.Empty : ": " + detail)}")
{
    public HttpStatusCode Status { get; } = status;

    public string Code { get; } = code;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The credential is gone (device retired, secret revoked): the agent must enroll again.</summary>
    public bool CredentialInvalid => Code == "DEVICE_INVALID_CREDENTIAL";
}

/// <summary>The agent's HTTPS calls: <c>POST /api/agent/v1/enroll</c> and <c>POST /api/agent/v1/token</c>.</summary>
public sealed class CloudHttpClient(HttpClient http)
{
    public const string HttpClientName = "monitor-cloud";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EnrollmentResult> EnrollAsync(string productKey, string fingerprint, CloudHost host, string? locationCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        var body = new
        {
            productKey, fingerprint, hostname = host.Hostname, osFamily = host.OsFamily, osName = host.OsName, osVersion = host.OsVersion,
            architecture = host.Architecture, agentVersion = host.AgentVersion, protocolVersion = 1, locationCode = string.IsNullOrWhiteSpace(locationCode) ? null : locationCode,
        };
        using var response = await http.PostAsJsonAsync(new Uri("api/agent/v1/enroll", UriKind.Relative), body, Json, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        var license = root.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
        return new EnrollmentResult(
            root.GetProperty("deviceId").GetGuid(),
            root.GetProperty("deviceSecret").GetString()!,
            root.TryGetProperty("gatewayUrl", out var g) ? g.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("tenantName", out var t) ? t.GetString() : null,
            root.TryGetProperty("locationName", out var n) ? n.GetString() : null,
            license.ValueKind == JsonValueKind.Object && license.TryGetProperty("state", out var s) ? s.GetString() : null,
            license.ValueKind == JsonValueKind.Object && license.TryGetProperty("token", out var tk) ? tk.GetString() : null,
            license.ValueKind == JsonValueKind.Object && license.TryGetProperty("checkAfter", out var c) && c.ValueKind == JsonValueKind.String ? c.GetDateTimeOffset() : null);
    }

    public async Task<DeviceToken> TokenAsync(Guid deviceId, string deviceSecret, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri("api/agent/v1/token", UriKind.Relative), new { deviceId, deviceSecret }, Json, cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        var seconds = root.TryGetProperty("expiresIn", out var e) ? e.GetInt32() : 3600;
        return new DeviceToken(root.GetProperty("accessToken").GetString()!, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
            return JsonDocument.Parse(text).RootElement.Clone();
        string code = "HTTP_" + (int)response.StatusCode;
        string? detail = null;
        try
        {
            using var problem = JsonDocument.Parse(text);
            if (problem.RootElement.TryGetProperty("code", out var c))
                code = c.GetString() ?? code;
            if (problem.RootElement.TryGetProperty("detail", out var d))
                detail = d.GetString();
        }
        catch (JsonException)
        {
            // Not a problem document (proxy page, gateway error).
        }

        throw new CloudRequestException(response.StatusCode, code, detail, response.Headers.RetryAfter?.Delta);
    }
}

/// <summary>Keeps a device token and renews it 5 minutes before it expires (05 section 1.2).</summary>
public sealed class DeviceTokenProvider(CloudHttpClient client, TimeProvider clock)
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);
    private DeviceToken? _token;
    private Guid _deviceId;

    public async Task<string> GetAsync(Guid deviceId, string deviceSecret, CancellationToken cancellationToken)
    {
        if (_token is { } token && _deviceId == deviceId && token.ExpiresAt - RenewBefore > clock.GetUtcNow())
            return token.AccessToken;
        _token = await client.TokenAsync(deviceId, deviceSecret, cancellationToken);
        _deviceId = deviceId;
        return _token.AccessToken;
    }

    public void Forget() => _token = null;
}
