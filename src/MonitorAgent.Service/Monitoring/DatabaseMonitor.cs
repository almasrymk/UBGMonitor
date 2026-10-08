using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Options;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using Microsoft.Extensions.Options;

namespace MonitorAgent.Service.Monitoring;

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
        UpdateTlsWarning(config.MonitorPoints, _health);
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

        _logger.LogInformation("[Database] Checking the local database...");
        var up = await CanConnectAsync(connectionString, cancellationToken);

        _health.SetPointHealth("local-database", up);
        if (up)
        {
            _health.ClearIssue("database");
            _logger.LogInformation("[Database] Local database: OK - connected");
        }
        else
        {
            _health.SetIssue("database", "Critical", "Database unreachable", IssueText.DatabaseDown(), "local-database");
            _logger.LogWarning("[Database] Local database: CRITICAL - connection failed");
        }
    }

    private async Task CheckPointAsync(MonitorPoint point, CancellationToken cancellationToken)
    {
        var login = point.Database;
        var key = $"database:{point.MonitorPointId}";
        if (login is null || string.IsNullOrWhiteSpace(login.Server) || string.IsNullOrWhiteSpace(login.Database))
        {
            _logger.LogWarning("[Database] {Name}: CRITICAL - connection details are not configured", point.DisplayName);
            _health.SetPointHealth(point.MonitorPointId, false, "Database connection is not configured", "Critical");
            _health.SetIssue(key, "Critical", "Database not configured", IssueText.DatabasePointDown(point.DisplayName, "Database", "-", "The connection details have not been saved."), point.MonitorPointId);
            return;
        }

        var engine = EngineLabel(login.Engine);
        _logger.LogInformation("[Database] Checking {Name} ({Engine} {Server}/{Database})...", point.DisplayName, engine, login.Server, login.Database);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var failure = await ConnectFailureAsync(login, cancellationToken);
        if (failure is null)
        {
            _logger.LogInformation("[Database] {Name}: OK - connected ({Ms} ms)", point.DisplayName, watch.ElapsedMilliseconds);
            _health.SetPointHealth(point.MonitorPointId, true, $"{engine} is reachable", "Healthy", watch.ElapsedMilliseconds);
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
        _logger.LogWarning("[Database] {Name}: CRITICAL - {Reason} ({Ms} ms)", point.DisplayName, failure, watch.ElapsedMilliseconds);
    }

    /// <summary>Null when the database answered; otherwise why it did not, with a hint for the usual causes.</summary>
    public static async Task<string?> ConnectFailureAsync(DatabaseLogin login, CancellationToken cancellationToken)
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var message = ex.Message.ReplaceLineEndings(" ").Trim();
            var hint = Hint(login, ex);
            if (hint is not null && message.IndexOf(". ", StringComparison.Ordinal) is > 0 and var end)
            {
                message = message[..(end + 1)];
            }

            message = message.Length > 240 ? message[..240] + "..." : message;
            return hint is null ? message : $"{message} {hint}";
        }
    }

    private static string? Hint(DatabaseLogin login, Exception ex)
    {
        var text = ex.Message;
        if (login.TlsMode == DatabaseTlsMode.Verify && (text.Contains("certificate", StringComparison.OrdinalIgnoreCase) || text.Contains("SSL", StringComparison.OrdinalIgnoreCase) || text.Contains("TLS", StringComparison.OrdinalIgnoreCase)))
            return "Server identity verification failed. Install a trusted certificate matching the server name, or explicitly choose Compatibility (server identity not verified).";
        var port = login.Port > 0 ? login.Port : DefaultPort(login.Engine);
        if (login.Engine == DatabaseEngine.SqlServer)
        {
            if (text.Contains("error: 26", StringComparison.OrdinalIgnoreCase) || text.Contains("Locating Server/Instance", StringComparison.OrdinalIgnoreCase))
            {
                return "Hint: for a named instance (server\\instance) start the SQL Server Browser service on the server, or enter the instance's TCP port.";
            }

            if (text.Contains("network-related", StringComparison.OrdinalIgnoreCase) || text.Contains("error: 40", StringComparison.OrdinalIgnoreCase)
                || text.Contains("was not found or was not accessible", StringComparison.OrdinalIgnoreCase) || ex is System.Net.Sockets.SocketException)
            {
                return $"Hint: connecting by IP or computer name needs TCP/IP. On the server open SQL Server Configuration Manager > SQL Server Network Configuration > Protocols, enable TCP/IP, restart the SQL Server service, and allow TCP port {port} in Windows Firewall.";
            }

            if (login.IntegratedSecurity && text.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Hint: with Windows authentication the service signs in as this computer's account (NT AUTHORITY\\SYSTEM); use a SQL Server login instead.";
            }

            return null;
        }

        return text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("connect", StringComparison.OrdinalIgnoreCase)
            ? $"Hint: check the server address, that the database server accepts connections from other computers, and that TCP port {port} is open in its firewall."
            : null;
    }

    private static int DefaultPort(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => 5432,
        DatabaseEngine.MySql => 3306,
        _ => 1433
    };

    /// <summary>Passwords are saved encrypted; one typed in the app arrives as plain text until it is saved.</summary>
    private static string Password(string? stored) =>
        stored is { Length: > 0 } && !SecretProtector.IsProtected(stored) ? stored : SecretProtector.Unprotect(stored);

    public static DbConnection CreateConnection(DatabaseLogin login)
    {
        if (!Enum.IsDefined(login.TlsMode) || !Enum.IsDefined(login.Engine)) throw new ArgumentException("Invalid database security mode.");
        var password = Password(login.Password);
        return login.Engine switch
        {
            DatabaseEngine.PostgreSql => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = login.Server,
                Port = login.Port > 0 ? login.Port : 5432,
                Database = login.Database,
                Username = login.Username,
                Password = password,
                SslMode = login.TlsMode == DatabaseTlsMode.Verify ? Npgsql.SslMode.VerifyFull : Npgsql.SslMode.Prefer,
                Timeout = 8
            }.ConnectionString),
            DatabaseEngine.MySql => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = login.Server,
                Port = (uint)(login.Port > 0 ? login.Port : 3306),
                Database = login.Database,
                UserID = login.Username,
                Password = password,
                SslMode = login.TlsMode == DatabaseTlsMode.Verify ? MySqlSslMode.VerifyFull : MySqlSslMode.Preferred,
                ConnectionTimeout = 8
            }.ConnectionString),
            _ => new SqlConnection(BuildSqlServer(login, password))
        };
    }

    private static string BuildSqlServer(DatabaseLogin login, string password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = login.Port is 0 or 1433 || login.Server.Contains(',') ? login.Server.Trim() : $"{login.Server.Trim()},{login.Port}",
            InitialCatalog = login.Database,
            IntegratedSecurity = login.IntegratedSecurity,
            // Older servers cannot do the TLS that the driver asks for by default.
            Encrypt = login.TlsMode == DatabaseTlsMode.Verify ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = login.TlsMode != DatabaseTlsMode.Verify,
            ConnectTimeout = 8
        };
        if (!login.IntegratedSecurity)
        {
            builder.UserID = login.Username;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
    public static void UpdateTlsWarning(IEnumerable<MonitorPoint> points, IMonitorHealthStore health)
    {
        var compatibility = points.Where(p => p.Type == MonitorPointType.Database && p.Database is { TlsMode: DatabaseTlsMode.Compatibility }).Select(p => p.DisplayName).ToList();
        if (compatibility.Count == 0) health.ClearIssue("security:database-tls");
        else
        {
            var message = "Compatibility mode: " + string.Join(", ", compatibility) + ". Test verification and fix the server certificate before switching explicitly.";
            if (health.GetIssues().FirstOrDefault(i => i.Id == "security:database-tls")?.Message != message)
                health.SetIssue("security:database-tls", "Warning", "Database server identity not verified", message);
        }
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
