using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Security;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class DatabaseMonitor : BackgroundService, IMonitoringModule
{
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<DatabaseMonitor> _logger;
    private readonly int _localIntervalSeconds;
    private readonly Dictionary<string, DateTime> _nextCheckUtc = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextLocalCheckUtc = DateTime.MinValue;

    public DatabaseMonitor(
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        IOptions<MonitoringOptions> options,
        ILogger<DatabaseMonitor> logger)
    {
        _configCache = configCache;
        _health = health;
        _logger = logger;
        _localIntervalSeconds = Math.Max(5, options.Value.DatabaseIntervalSeconds);
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

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var points = config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Database).ToList();
        var active = new HashSet<string>(points.Select(p => p.MonitorPointId), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _nextCheckUtc.Keys.Where(id => !active.Contains(id)).ToList())
        {
            _health.ClearIssue($"database:{id}");
            _nextCheckUtc.Remove(id);
        }

        var now = DateTime.UtcNow;
        foreach (var point in points)
        {
            if (_nextCheckUtc.TryGetValue(point.MonitorPointId, out var due) && now < due)
            {
                continue;
            }

            await CheckPointAsync(point, cancellationToken);
            _nextCheckUtc[point.MonitorPointId] = DateTime.UtcNow.AddSeconds(Math.Max(1, point.IntervalSeconds));
        }

        if (now < _nextLocalCheckUtc)
        {
            return;
        }

        _nextLocalCheckUtc = DateTime.UtcNow.AddSeconds(_localIntervalSeconds);
        var connectionString = config.DatabaseConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var up = await CanConnectAsync(connectionString, cancellationToken);

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

    private async Task CheckPointAsync(MonitorPoint point, CancellationToken cancellationToken)
    {
        var login = point.Database;
        var key = $"database:{point.MonitorPointId}";
        if (login is null || string.IsNullOrWhiteSpace(login.Server) || string.IsNullOrWhiteSpace(login.Database))
        {
            _health.SetPointHealth(point.MonitorPointId, false, "Database connection is not configured", "Critical");
            _health.SetIssue(key, "Critical", "Database not configured", IssueText.DatabasePointDown(point.DisplayName, "Database", "-", "لم يتم حفظ بيانات الاتصال."), point.MonitorPointId);
            return;
        }

        var failure = await ConnectFailureAsync(login, cancellationToken);
        var engine = EngineLabel(login.Engine);
        if (failure is null)
        {
            _health.SetPointHealth(point.MonitorPointId, true, $"{engine} is reachable", "Healthy");
            _health.ClearIssue(key);
            return;
        }

        _health.SetPointHealth(point.MonitorPointId, false, failure, "Critical");
        _health.SetIssue(
            key,
            "Critical",
            "Database unreachable",
            IssueText.DatabasePointDown(point.DisplayName, engine, login.Server, failure),
            point.MonitorPointId);
        _logger.LogWarning("Database {Name} ({Engine} {Server}) is unreachable: {Reason}", point.DisplayName, engine, login.Server, failure);
    }

    private static async Task<string?> ConnectFailureAsync(DatabaseLogin login, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = CreateConnection(login);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            var message = ex.Message.ReplaceLineEndings(" ").Trim();
            return message.Length > 240 ? message[..240] : message;
        }
    }

    private static DbConnection CreateConnection(DatabaseLogin login)
    {
        var password = SecretProtector.Unprotect(login.Password);
        return login.Engine switch
        {
            DatabaseEngine.PostgreSql => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = login.Server,
                Port = login.Port > 0 ? login.Port : 5432,
                Database = login.Database,
                Username = login.Username,
                Password = password,
                Timeout = 8
            }.ConnectionString),
            DatabaseEngine.MySql => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = login.Server,
                Port = (uint)(login.Port > 0 ? login.Port : 3306),
                Database = login.Database,
                UserID = login.Username,
                Password = password,
                ConnectionTimeout = 8
            }.ConnectionString),
            _ => new SqlConnection(BuildSqlServer(login, password))
        };
    }

    private static string BuildSqlServer(DatabaseLogin login, string password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = login.Port is 0 or 1433 ? login.Server : $"{login.Server},{login.Port}",
            InitialCatalog = login.Database,
            IntegratedSecurity = login.IntegratedSecurity,
            TrustServerCertificate = true,
            ConnectTimeout = 8
        };
        if (!login.IntegratedSecurity)
        {
            builder.UserID = login.Username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private static string EngineLabel(DatabaseEngine engine)
        => engine switch
        {
            DatabaseEngine.PostgreSql => "PostgreSQL",
            DatabaseEngine.MySql => "MySQL",
            _ => "SQL Server"
        };

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
