using System.Text.Json;
using System.Text.Json.Serialization;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace ClientAgent.Service.Config;

public interface ILocalConfigCache
{
    Task<AgentRuntimeConfig> GetConfigAsync(CancellationToken cancellationToken = default);

    Task SaveConfigAsync(AgentRuntimeConfig config, CancellationToken cancellationToken = default);

    string GetConfigVersion();

    DateTime? GetLastSyncUtc();

    GeneralRuntimeSettings GetGeneral();
}

public sealed class LocalConfigCache : ILocalConfigCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly IAgentIdentity _identity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalConfigCache> _logger;
    private static readonly JsonSerializerOptions AppSettingsJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentRuntimeConfig _current;
    private DateTime _appSettingsStamp = DateTime.MinValue;
    private List<MonitorPoint>? _appSettingsPoints;
    private GeneralRuntimeSettings _general = new();

    public LocalConfigCache(IAgentIdentity identity, IConfiguration configuration, ILogger<LocalConfigCache> logger)
    {
        _identity = identity;
        _configuration = configuration;
        _logger = logger;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClientAgent",
            "config.json");
        _current = CreateDefault();
    }

    public async Task<AgentRuntimeConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_path))
            {
                var json = await File.ReadAllTextAsync(_path, cancellationToken);
                var loaded = JsonSerializer.Deserialize<AgentRuntimeConfig>(json, JsonOptions);
                if (loaded is not null)
                {
                    _current = UpgradeLegacySample(loaded);
                    if (!ReferenceEquals(_current, loaded))
                    {
                        await SaveUnlockedAsync(_current, cancellationToken);
                    }
                }
            }
            else
            {
                await SaveUnlockedAsync(_current, cancellationToken);
            }

            return ApplyConfiguredMonitorPoints(_current);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read local config cache");
            return ApplyConfiguredMonitorPoints(_current);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveConfigAsync(AgentRuntimeConfig config, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveUnlockedAsync(config, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public string GetConfigVersion() => _current.ConfigVersion;

    public GeneralRuntimeSettings GetGeneral()
    {
        TryReadMonitorPoints(out _);
        return _general;
    }

    public DateTime? GetLastSyncUtc()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(_path);
    }

    private async Task SaveUnlockedAsync(AgentRuntimeConfig config, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(config, JsonOptions), cancellationToken);
        _current = config;
    }

    private AgentRuntimeConfig ApplyConfiguredMonitorPoints(AgentRuntimeConfig config)
    {
        if (!TryReadMonitorPoints(out var configured))
        {
            var section = _configuration.GetSection("Setting:MonitorPoints");
            if (!section.Exists())
            {
                section = _configuration.GetSection("MonitorPoints");
            }

            if (!section.Exists())
            {
                return config;
            }

            configured = section.Get<List<MonitorPoint>>() ?? [];
        }

        return new AgentRuntimeConfig
        {
            ConfigVersion = config.ConfigVersion,
            AgentId = config.AgentId,
            MonitorPoints = configured,
            DatabaseConnectionString = config.DatabaseConnectionString,
            CpuCriticalThreshold = config.CpuCriticalThreshold,
            RamCriticalThreshold = config.RamCriticalThreshold,
            DiskCriticalThreshold = config.DiskCriticalThreshold
        };
    }

    private bool TryReadMonitorPoints(out List<MonitorPoint> points)
    {
        points = [];
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_appSettingsPoints is not null && stamp == _appSettingsStamp)
            {
                points = _appSettingsPoints;
                return true;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TryGetProperty(document.RootElement, "Setting", out var setting)
                || !TryGetProperty(setting, "MonitorPoints", out var monitorPoints))
            {
                if (!TryGetProperty(document.RootElement, "MonitorPoints", out monitorPoints))
                {
                    return false;
                }
            }

            points = monitorPoints.Deserialize<List<MonitorPoint>>(AppSettingsJson) ?? [];
            if (TryGetProperty(document.RootElement, "Setting", out var settingNode)
                && TryGetProperty(settingNode, "General", out var generalNode))
            {
                _general = generalNode.Deserialize<GeneralRuntimeSettings>(AppSettingsJson) ?? new GeneralRuntimeSettings();
            }

            _appSettingsStamp = stamp;
            _appSettingsPoints = points;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not read monitor points from appsettings.json");
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private AgentRuntimeConfig CreateDefault()
    {
        return new AgentRuntimeConfig
        {
            ConfigVersion = "1",
            AgentId = _identity.AgentId,
            CpuCriticalThreshold = 90,
            RamCriticalThreshold = 90,
            DiskCriticalThreshold = 90,
            DatabaseConnectionString = $"Data Source={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClientAgent", "local.db")}",
            MonitorPoints =
            [
                new MonitorPoint
                {
                    MonitorPointId = "main-point-a",
                    DisplayName = "Main Point A",
                    Type = MonitorPointType.Device,
                    Address = "127.0.0.1",
                    Location = "HQ",
                    Model = "Server",
                    Enabled = true,
                    IntervalSeconds = 15
                },
                new MonitorPoint
                {
                    MonitorPointId = "regional-point-b",
                    DisplayName = "Regional Point B",
                    Type = MonitorPointType.Device,
                    Address = "127.0.0.1",
                    Location = "Region",
                    Model = "Satellite",
                    Enabled = true,
                    IntervalSeconds = 15
                },
                new MonitorPoint
                {
                    MonitorPointId = "remote-point-c",
                    DisplayName = "Remote Point C",
                    Type = MonitorPointType.Device,
                    Address = "192.0.2.1",
                    Location = "Remote",
                    Model = "IP Camera",
                    Enabled = true,
                    IntervalSeconds = 15
                }
            ]
        };
    }

    private AgentRuntimeConfig UpgradeLegacySample(AgentRuntimeConfig loaded)
    {
        var ids = loaded.MonitorPoints.Select(p => p.MonitorPointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!ids.Contains("camera-01") || ids.Count > 2)
        {
            return loaded;
        }

        var upgraded = CreateDefault();
        return new AgentRuntimeConfig
        {
            ConfigVersion = loaded.ConfigVersion,
            AgentId = string.IsNullOrWhiteSpace(loaded.AgentId) ? upgraded.AgentId : loaded.AgentId,
            CpuCriticalThreshold = loaded.CpuCriticalThreshold,
            RamCriticalThreshold = loaded.RamCriticalThreshold,
            DiskCriticalThreshold = loaded.DiskCriticalThreshold,
            DatabaseConnectionString = loaded.DatabaseConnectionString,
            MonitorPoints = upgraded.MonitorPoints
        };
    }
}
