using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Models;

namespace MonitorAgent.UI.Services;

public sealed record ServiceSettings(UiAppSettings Settings, List<MonitorPoint> MonitorPoints);

/// <summary>Loads and saves the settings through the Agent service, which owns its appsettings.json.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AgentApiClient _client;

    public AppSettingsStore(AgentApiClient client)
    {
        _client = client;
    }

    /// <summary>Null when the service did not answer.</summary>
    public async Task<ServiceSettings?> LoadAsync()
    {
        if (await _client.GetSettingsAsync() is not { } section)
        {
            return null;
        }

        try
        {
            var settings = section.Deserialize<UiAppSettings>(JsonOptions) ?? new UiAppSettings();
            if (section["General"] is null && section["RefreshInterval"] is not null)
            {
                settings.General = section.Deserialize<GeneralSettings>(JsonOptions) ?? settings.General;
            }

            var points = section["MonitorPoints"]?.Deserialize<List<MonitorPoint>>(JsonOptions) ?? [];
            return new ServiceSettings(settings, points);
        }
        catch (JsonException)
        {
            return new ServiceSettings(new UiAppSettings(), []);
        }
    }

    /// <exception cref="IOException">The service did not save the settings; the message says why.</exception>
    public async Task SaveAsync(UiAppSettings settings, IReadOnlyList<MonitorPoint> monitorPoints, RemoteAccessAction? remoteAccess = null)
    {
        if (remoteAccess is not null)
        {
            if (!_client.CanAdminister || !_client.IsLocalTransport)
                throw new IOException("Remote access settings require local Administrator access.");
            var status = await _client.GetRemoteAccessAsync() ?? throw new IOException("Could not check remote access settings. Nothing was saved.");
            if (remoteAccess.Enabled && !status.HasViewerKey)
                throw new IOException("Create a viewer key before saving enabled remote access. Nothing was saved.");
            if (remoteAccess.AllowAdministration && !status.HasAdminKey)
                throw new IOException("Create a separate admin key before saving remote administration. Nothing was saved.");
        }
        settings.MonitorPoints = monitorPoints.ToList();
        var section = JsonSerializer.SerializeToNode(settings, JsonOptions) as JsonObject ?? [];
        if (await _client.SaveSettingsAsync(section) is { } error)
        {
            throw new IOException(error);
        }
        if (remoteAccess is not null && await _client.ManageRemoteAccessAsync(remoteAccess) is null)
            throw new IOException("General settings were saved, but remote access could not be applied. Check the service and retry Save.");
    }
}

/// <summary>
/// The only settings the app keeps itself: which service to show (and its access key) and the theme, so the
/// window looks right and can connect before the service has answered. Each computer's app keeps its own.
/// </summary>
public sealed class ClientPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorAgent", "client.json");

    /// <summary>The service this app shows: on this computer or on another one.</summary>
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5050";

    /// <summary>The access key of that service, when it is on another computer and has one.</summary>
    [JsonIgnore]
    public string AccessKey { get; set; } = string.Empty;

    [JsonPropertyName("AccessKey")]
    public string StoredAccessKey
    {
        get => SecretProtector.Protect(AccessKey);
        set => AccessKey = SecretProtector.IsProtected(value) ? SecretProtector.Unprotect(value) : value ?? string.Empty;
    }

    public string Theme { get; set; } = "Dark";
    [JsonIgnore]
    public Dictionary<string, string> CertificatePins { get; set; } = new();
    [JsonPropertyName("CertificatePins")]
    public string StoredCertificatePins
    {
        get => CertificatePins.Count == 0 ? "" : SecretProtector.Protect(JsonSerializer.Serialize(CertificatePins));
        set
        {
            var plain = SecretProtector.Unprotect(value);
            CertificatePins = plain.Length == 0 ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
    }

    /// <summary>Before the rename the app's folder was "AgentMonitor"; moves it once so the layout and preferences stay.</summary>
    public static void MoveLegacyFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacy = Path.Combine(local, "AgentMonitor");
        var current = Path.Combine(local, "MonitorAgent");
        try
        {
            if (Directory.Exists(legacy) && !Directory.Exists(current))
            {
                Directory.Move(legacy, current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static ClientPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ClientPreferences();
            var text = File.ReadAllText(FilePath);
            var preferences = JsonSerializer.Deserialize<ClientPreferences>(text) ?? new ClientPreferences();
            if (text.Contains("dpapi:", StringComparison.Ordinal) && preferences.AccessKey.Length > 0)
            {
                PrivateFile.Backup(FilePath, Path.Combine(Path.GetDirectoryName(FilePath)!, "backups"));
                preferences.Save();
            }
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClientPreferences();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            PrivateFile.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
