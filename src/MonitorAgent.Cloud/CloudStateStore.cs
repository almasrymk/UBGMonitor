using System.Text.Json;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Cloud;

/// <summary>What the agent keeps after enrollment. The device secret is stored encrypted with <see cref="SecretProtector"/>.</summary>
public sealed record CloudState
{
    public Guid DeviceId { get; init; }

    public string? ProtectedDeviceSecret { get; init; }

    public string? GatewayUrl { get; init; }

    public string? TenantName { get; init; }

    public string? LocationName { get; init; }

    public DateTimeOffset? EnrolledAt { get; init; }

    public string? LicenseState { get; init; }

    public string? LicenseToken { get; init; }

    /// <summary>The cloud's public command-signing keys (JWK set) from enrollment; not secret.</summary>
    public string? CommandSigningKeys { get; init; }

    public bool IsEnrolled => DeviceId != Guid.Empty && !string.IsNullOrEmpty(ProtectedDeviceSecret);
}

/// <summary><c>cloud.json</c> next to the licence file (05 section 10).</summary>
public sealed class CloudStateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    public CloudStateStore(string folder)
    {
        _path = Path.Combine(folder, "cloud.json");
    }

    public CloudState Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(_path) ? JsonSerializer.Deserialize<CloudState>(File.ReadAllText(_path), Json) ?? new CloudState() : new CloudState();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new CloudState();
            }
        }
    }

    public void Save(CloudState state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, _path, overwrite: true);
        }
    }

    public static string? Secret(CloudState state) => SecretProtector.Unprotect(state.ProtectedDeviceSecret) is { Length: > 0 } secret ? secret : null;

    public static CloudState Enrolled(CloudState previous, EnrollmentResult result) => previous with
    {
        DeviceId = result.DeviceId,
        ProtectedDeviceSecret = SecretProtector.Protect(result.DeviceSecret),
        GatewayUrl = result.GatewayUrl,
        TenantName = result.TenantName,
        LocationName = result.LocationName,
        EnrolledAt = DateTimeOffset.UtcNow,
        LicenseState = result.LicenseState,
        LicenseToken = result.LicenseToken,
        CommandSigningKeys = result.CommandSigningKeys ?? previous.CommandSigningKeys,
    };
}
