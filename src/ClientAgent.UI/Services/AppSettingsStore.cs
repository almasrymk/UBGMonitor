using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Security;
using ClientAgent.UI.Models;

namespace ClientAgent.UI.Services;

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
    public async Task SaveAsync(UiAppSettings settings, IReadOnlyList<MonitorPoint> monitorPoints)
    {
        settings.MonitorPoints = monitorPoints.ToList();
        var section = JsonSerializer.SerializeToNode(settings, JsonOptions) as JsonObject ?? [];
        if (await _client.SaveSettingsAsync(section) is { } error)
        {
            throw new IOException(error);
        }
    }
}

/// <summary>
/// The only settings the app keeps itself: which service to show (and its access key) and the theme, so the
/// window looks right and can connect before the service has answered. Each computer's app keeps its own.
/// </summary>
public sealed class ClientPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentMonitor", "client.json");

    /// <summary>The service this app shows: on this computer or on another one.</summary>
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5050";

    /// <summary>The access key of that service, when it is on another computer and has one.</summary>
    [JsonIgnore]
    public string AccessKey { get; set; } = string.Empty;

    [JsonPropertyName("AccessKey")]
    public string StoredAccessKey
    {
        get => SecretProtector.Protect(AccessKey);
        set => AccessKey = value?.StartsWith("dpapi:", StringComparison.Ordinal) == true ? SecretProtector.Unprotect(value) : value ?? string.Empty;
    }

    public string Theme { get; set; } = "Dark";

    public static ClientPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(FilePath)) ?? new ClientPreferences()
                : new ClientPreferences();
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
