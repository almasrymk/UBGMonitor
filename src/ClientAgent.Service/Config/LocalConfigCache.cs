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
                    _current = loaded;
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
                    MonitorPointId = "camera-01",
                    DisplayName = "Entrance Camera",
                    Type = MonitorPointType.Device,
                    Address = "127.0.0.1",
                    Location = "Gate",
                    Model = "IP Camera",
                    Enabled = true,
                    IntervalSeconds = 15
                },
                new MonitorPoint
                {
                    MonitorPointId = "barrier-01",
                    DisplayName = "Barrier Gate",
                    Type = MonitorPointType.Device,
                    Address = "127.0.0.1",
                    Location = "Gate",
                    Model = "Barrier",
                    Enabled = true,
                    IntervalSeconds = 15
                }
            ]
        };
    }
}
