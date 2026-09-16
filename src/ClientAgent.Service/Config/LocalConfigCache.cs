using System.Text.Json;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Config;

public interface ILocalConfigCache
{
    Task<AgentRuntimeConfig> GetConfigAsync(CancellationToken cancellationToken = default);

    Task SaveConfigAsync(AgentRuntimeConfig config, CancellationToken cancellationToken = default);

    string GetConfigVersion();

    DateTime? GetLastSyncUtc();
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
    private readonly ILogger<LocalConfigCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentRuntimeConfig _current;

    public LocalConfigCache(IAgentIdentity identity, ILogger<LocalConfigCache> logger)
    {
        _identity = identity;
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

            return _current;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read local config cache");
            return _current;
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
