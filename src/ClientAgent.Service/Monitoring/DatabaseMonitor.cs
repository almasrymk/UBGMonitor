using Microsoft.Data.Sqlite;
using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class DatabaseMonitor : BackgroundService, IMonitoringModule
{
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<DatabaseMonitor> _logger;
    private readonly int _intervalSeconds;

    public DatabaseMonitor(
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        IOptions<MonitoringOptions> options,
        ILogger<DatabaseMonitor> logger)
    {
        _configCache = configCache;
        _health = health;
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
        foreach (var point in config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Database))
        {
            _health.SetPointHealth(point.MonitorPointId, up);
        }

        _health.SetPointHealth("local-database", up);
        if (up)
        {
            _health.ClearIssue("database");
            _logger.LogDebug("Local database is reachable");
        }
        else
        {
            _health.SetIssue("database", "Critical", "Database unreachable", IssueText.DatabaseDown(), "local-database");
            _logger.LogWarning("Local database connection failed");
        }
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
