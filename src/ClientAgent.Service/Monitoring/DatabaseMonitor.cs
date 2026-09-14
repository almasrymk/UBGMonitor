using Microsoft.Data.Sqlite;
using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class DatabaseMonitor : BackgroundService, IMonitoringModule
{
    private readonly IEventPublisher _publisher;
    private readonly IAgentIdentity _identity;
    private readonly ILocalConfigCache _configCache;
    private readonly ILogger<DatabaseMonitor> _logger;
    private readonly int _intervalSeconds;

    public DatabaseMonitor(
        IEventPublisher publisher,
        IAgentIdentity identity,
        ILocalConfigCache configCache,
        IOptions<MonitoringOptions> options,
        ILogger<DatabaseMonitor> logger)
    {
        _publisher = publisher;
        _identity = identity;
        _configCache = configCache;
        _logger = logger;
        _intervalSeconds = Math.Max(5, options.Value.DatabaseIntervalSeconds);
    }

    public string Name => nameof(DatabaseMonitor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var connectionString = config.DatabaseConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var up = await CanConnectAsync(connectionString, cancellationToken);
        await _publisher.PublishAsync(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _identity.AgentId,
            MonitorPointId = "local-database",
            TimestampUtc = DateTime.UtcNow,
            EventType = EventType.DatabaseStatus,
            Severity = up ? Severity.Info : Severity.Critical,
            Status = up ? EventStatus.Up : EventStatus.Down,
            Message = up ? "Local database is reachable" : "Local database connection failed",
            ConfigVersion = _configCache.GetConfigVersion(),
            AgentVersion = _identity.Version
        }, cancellationToken);
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
