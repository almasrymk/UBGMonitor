using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Licensing;

/// <summary>
/// Built into the program. Only the product code and the API client of the licensing site are read from appsettings.json
/// (<see cref="CredentialsSection"/>); the site and the rules cannot be changed there, and licensing cannot be turned off.
/// </summary>
public sealed class LicensingOptions
{
    public const string CredentialsSection = "LicensingClient";

    public string PlatformUrl { get; init; } = "https://almasrymk-001-site15.etempurl.com";

    public const string DefaultProductCode = "000001";

    /// <summary>The MonitorAgent product's code on the licensing site; fixed there once the product is created.</summary>
    public string ProductCode { get; init; } = DefaultProductCode;

    /// <summary>Legacy installation configuration; empty credentials leave online operations unavailable.</summary>
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Optional installation-specific credential, supplied by an administrator. Never shipped.</summary>
    public DeviceEnrollmentCredential? DeviceCredential { get; init; }

    /// <summary>Kept below the app's 20-second request timeout, so an activation answer reaches the app.</summary>
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>How long the service keeps working without reaching the server, when the signed token does not say.</summary>
    public int OfflineGraceDays { get; init; } = 7;

    /// <summary>The secret may be written plain or protected for this computer (dpapi: / aes:), like the database passwords.</summary>
    public static LicensingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(CredentialsSection);
        var secret = section["ClientSecret"]?.Trim() ?? string.Empty;
        return new LicensingOptions
        {
            ProductCode = section["ProductCode"]?.Trim() is { Length: > 0 } code ? code : DefaultProductCode,
            ClientId = section["ClientId"]?.Trim() ?? string.Empty,
            ClientSecret = SecretProtector.IsProtected(secret) ? SecretProtector.Unprotect(secret) : secret,
            DeviceCredential = configuration.GetValue<bool>("DeviceEnrollment:Enabled")
                ? new DeviceEnrollmentCredential(
                    configuration["DeviceEnrollment:DeviceId"] ?? "",
                    configuration["DeviceEnrollment:ProductCode"] ?? "",
                    configuration["DeviceEnrollment:PlatformUrl"] ?? "",
                    configuration["DeviceEnrollment:ClientId"] ?? "",
                    configuration["DeviceEnrollment:ClientSecret"] ?? "")
                : null
        };
    }
}

// Deliberately not a record: its generated ToString would expose the credential.
public sealed class DeviceEnrollmentCredential(string deviceId, string productCode, string platformUrl, string clientId, string clientSecret)
{
    public string DeviceId { get; } = deviceId;
    public string ProductCode { get; } = productCode;
    public string PlatformUrl { get; } = platformUrl;
    public string ClientId { get; } = clientId;
    public string ClientSecret { get; } = clientSecret;

    public bool Matches(string device, LicensingOptions options) =>
        DeviceId == device && ProductCode == options.ProductCode &&
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) &&
        Uri.TryCreate(PlatformUrl, UriKind.Absolute, out var enrolled) &&
        Uri.TryCreate(options.PlatformUrl, UriKind.Absolute, out var configured) &&
        enrolled.Scheme == Uri.UriSchemeHttps && enrolled == configured &&
        string.IsNullOrEmpty(enrolled.UserInfo) && string.IsNullOrEmpty(enrolled.Query) && string.IsNullOrEmpty(enrolled.Fragment);
}
