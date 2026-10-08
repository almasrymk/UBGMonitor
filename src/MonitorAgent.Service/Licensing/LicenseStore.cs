using System.Text.Json;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Licensing;

/// <summary>What the service remembers about its license between restarts.</summary>
public sealed record StoredLicense
{
    /// <summary>Encrypted with <see cref="SecretProtector"/>; read it with <see cref="LicenseStore.ProductKey"/>.</summary>
    public string? ProtectedProductKey { get; init; }

    public string? KeyPrefix { get; init; }

    /// <summary>The last signed token from the server; its signature is checked every time it is used.</summary>
    public string? Token { get; init; }

    public string? SigningKeysJson { get; init; }

    public DateTime? LastOnlineUtc { get; init; }

    /// <summary>The latest time this computer's clock showed, to notice the clock being moved back.</summary>
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>The last refusal from the server, kept so it still shows after a restart without internet.</summary>
    public LicenseState? RejectedState { get; init; }

    public string? RejectedCode { get; init; }

    public string? RejectedMessage { get; init; }
}

public interface ILicenseStore
{
    StoredLicense Load();

    void Save(StoredLicense license);
}

public sealed class LicenseStore : ILicenseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<LicenseStore> _logger;

    public LicenseStore(ILogger<LicenseStore> logger)
        : this(Path.Combine(AgentPaths.StateFolder, "license.json"), logger)
    {
    }

    public LicenseStore(string path, ILogger<LicenseStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public static string? ProductKey(StoredLicense license)
        => SecretProtector.Unprotect(license.ProtectedProductKey) is { Length: > 0 } key ? key : null;

    public StoredLicense Load()
    {
        try
        {
            var license = File.Exists(_path)
                ? JsonSerializer.Deserialize<StoredLicense>(File.ReadAllText(_path), JsonOptions) ?? new StoredLicense()
                : new StoredLicense();
            if (license.ProtectedProductKey is { } key && SecretProtector.IsLegacy(key))
            {
                var replacement = SecretProtector.ReprotectLegacy(key);
                if (replacement != key) { PrivateFile.Backup(_path, Path.Combine(Path.GetDirectoryName(_path)!, "backups")); license = license with { ProtectedProductKey = replacement }; Save(license); }
            }
            return license;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("[License] Could not read stored license: {ErrorType}", ex.GetType().Name);
            return new StoredLicense();
        }
    }

    public void Save(StoredLicense license)
    {
        try
        {
            PrivateFile.WriteAllText(_path, JsonSerializer.Serialize(license, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("[License] Could not save stored license: {ErrorType}", ex.GetType().Name);
        }
    }
}
