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

    /// <summary>The licensing site's API client (Integrations › API Clients); empty sends the device requests without a token.</summary>
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

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
            ClientSecret = SecretProtector.IsProtected(secret) ? SecretProtector.Unprotect(secret) : secret
        };
    }
}
