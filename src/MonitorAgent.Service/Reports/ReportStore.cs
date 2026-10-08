using MonitorAgent.Service.Config;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Shared.Models;
using Microsoft.Data.Sqlite;

namespace MonitorAgent.Service.Reports;

/// <summary>One reading of the device, saved every minute. Null means the value could not be read.</summary>
/// <param name="ReceivedMb">Downloaded by all adapters since the previous reading.</param>
/// <param name="DiskReadMbps">Average of the minute, in megabytes per second.</param>
public sealed record SampleRow(
    DateTime Utc, double Cpu, double Ram, double? TemperatureC, bool? Internet, bool? Network,
    double? PingMs = null, double? LossPercent = null, int? WifiSignal = null,
    double? ReceivedMb = null, double? SentMb = null,
    double? DiskActivePercent = null, double? DiskReadMbps = null, double? DiskWriteMbps = null, double? DiskResponseMs = null);

public sealed record DiskSampleRow(DateTime Utc, string Drive, double TotalGb, double FreeGb);

public sealed record PointSampleRow(DateTime Utc, string PointId, string Name, string Type, string Status, double? ResponseMs = null);

/// <param name="Kind">"cpu" (Value in %) or "ram" (Value in MB).</param>
public sealed record ProcessSampleRow(DateTime Utc, string Kind, string Name, double Value);

public sealed record SpeedTestRow(DateTime Utc, double? DownloadMbps, double? UploadMbps, string? Error);

public sealed record IncidentRow(
    long Id, string IssueId, string Source, string Severity, string Title, string Message,
    DateTime StartedUtc, DateTime? EndedUtc, string? EndedBy);

public sealed record SettingsChangeRow(DateTime Utc, string Change);

/// <param name="Area">"Programs", "Users" or "Services".</param>
/// <param name="Severity">"Critical", "Warning" or "Info".</param>
public sealed record ApplicationChangeRow(DateTime Utc, string Area, string Severity, string Change);

/// <summary>
/// The history behind the reports, in Data\reports.db: a reading every minute, every problem from start to end,
/// every speed test and every settings change. <see cref="DataCleaner"/> removes rows older than the "Keep data for" setting.
/// </summary>
public sealed class ReportStore
{
    /// <summary>The longest history that can be kept (the "Keep data for" setting decides the actual length).</summary>
    public const int KeepDays = GeneralRuntimeSettings.MaxRetentionDays;

    private static readonly string[] SampleColumns =
        ["ping_ms", "loss", "wifi", "rx_mb", "tx_mb", "disk_active", "disk_read", "disk_write", "disk_ms"];

    private readonly object _writeLock = new();
    private readonly ILogger<ReportStore> _logger;
    private readonly string _connectionString;

    public ReportStore(ILogger<ReportStore> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(IssueDataLogger.DataFolder);
        if (!File.Exists(DatabasePath)) MonitorAgent.Shared.Security.PrivateFile.WriteAllBytes(DatabasePath, []);
        else MonitorAgent.Shared.Security.PrivateFile.Secure(DatabasePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
        Execute("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS samples (ts INTEGER NOT NULL, cpu REAL, ram REAL, temp REAL, internet INTEGER, network INTEGER);
            CREATE INDEX IF NOT EXISTS ix_samples_ts ON samples(ts);
            CREATE TABLE IF NOT EXISTS disk_samples (ts INTEGER NOT NULL, drive TEXT, total_gb REAL, free_gb REAL);
            CREATE INDEX IF NOT EXISTS ix_disk_samples_ts ON disk_samples(ts);
            CREATE TABLE IF NOT EXISTS point_samples (ts INTEGER NOT NULL, point_id TEXT, name TEXT, type TEXT, status TEXT);
            CREATE INDEX IF NOT EXISTS ix_point_samples_ts ON point_samples(ts);
            CREATE TABLE IF NOT EXISTS process_samples (ts INTEGER NOT NULL, kind TEXT, name TEXT, value REAL);
            CREATE INDEX IF NOT EXISTS ix_process_samples_ts ON process_samples(ts);
            CREATE TABLE IF NOT EXISTS speed_tests (ts INTEGER NOT NULL, download REAL, upload REAL, error TEXT);
            CREATE TABLE IF NOT EXISTS incidents (id INTEGER PRIMARY KEY AUTOINCREMENT, issue_id TEXT NOT NULL, source TEXT,
                severity TEXT, title TEXT, message TEXT, started INTEGER NOT NULL, ended INTEGER, ended_by TEXT);
            CREATE INDEX IF NOT EXISTS ix_incidents_open ON incidents(issue_id, ended);
            CREATE TABLE IF NOT EXISTS settings_changes (ts INTEGER NOT NULL, change TEXT);
            CREATE TABLE IF NOT EXISTS app_changes (ts INTEGER NOT NULL, area TEXT, severity TEXT, change TEXT);
            CREATE INDEX IF NOT EXISTS ix_app_changes_ts ON app_changes(ts);
            """);
        Write(connection =>
        {
            foreach (var column in SampleColumns)
            {
                AddColumnIfMissing(connection, "samples", column);
            }

            AddColumnIfMissing(connection, "point_samples", "response_ms");
        });

        // Problems still open from the last run: the service stopped, so their real end is unknown.
        Execute("UPDATE incidents SET ended = $now, ended_by = 'service restart' WHERE ended IS NULL",
            ("$now", ToUnix(DateTime.UtcNow)));
    }

    public static string DatabasePath => Path.Combine(IssueDataLogger.DataFolder, "reports.db");

    public void AddSample(
        SampleRow sample,
        IEnumerable<(string Drive, double TotalGb, double FreeGb)> disks,
        IEnumerable<(string Id, string Name, string Type, string Status, double? ResponseMs)> points,
        IEnumerable<(string Kind, string Name, double Value)> processes)
    {
        Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            var ts = ToUnix(sample.Utc);
            Run(connection, """
                INSERT INTO samples (ts, cpu, ram, temp, internet, network, ping_ms, loss, wifi, rx_mb, tx_mb, disk_active, disk_read, disk_write, disk_ms)
                VALUES ($ts, $cpu, $ram, $temp, $internet, $network, $ping, $loss, $wifi, $rx, $tx, $active, $read, $write, $diskms)
                """,
                ("$ts", ts), ("$cpu", sample.Cpu), ("$ram", sample.Ram), ("$temp", sample.TemperatureC),
                ("$internet", ToInt(sample.Internet)), ("$network", ToInt(sample.Network)),
                ("$ping", sample.PingMs), ("$loss", sample.LossPercent), ("$wifi", sample.WifiSignal),
                ("$rx", sample.ReceivedMb), ("$tx", sample.SentMb),
                ("$active", sample.DiskActivePercent), ("$read", sample.DiskReadMbps), ("$write", sample.DiskWriteMbps), ("$diskms", sample.DiskResponseMs));
            foreach (var disk in disks)
            {
                Run(connection, "INSERT INTO disk_samples VALUES ($ts, $drive, $total, $free)",
                    ("$ts", ts), ("$drive", disk.Drive), ("$total", disk.TotalGb), ("$free", disk.FreeGb));
            }

            foreach (var point in points)
            {
                Run(connection, "INSERT INTO point_samples (ts, point_id, name, type, status, response_ms) VALUES ($ts, $id, $name, $type, $status, $ms)",
                    ("$ts", ts), ("$id", point.Id), ("$name", point.Name), ("$type", point.Type), ("$status", point.Status), ("$ms", point.ResponseMs));
            }

            foreach (var process in processes)
            {
                Run(connection, "INSERT INTO process_samples VALUES ($ts, $kind, $name, $value)",
                    ("$ts", ts), ("$kind", process.Kind), ("$name", process.Name), ("$value", process.Value));
            }

            transaction.Commit();
        });
    }

    public void AddSpeedTest(DateTime utc, double? download, double? upload, string? error) =>
        Write(connection => Run(connection, "INSERT INTO speed_tests VALUES ($ts, $down, $up, $error)",
            ("$ts", ToUnix(utc)), ("$down", download), ("$up", upload), ("$error", error)));

    public void AddSettingsChanges(DateTime utc, IEnumerable<string> changes) =>
        Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            foreach (var change in changes)
            {
                Run(connection, "INSERT INTO settings_changes VALUES ($ts, $change)", ("$ts", ToUnix(utc)), ("$change", change));
            }

            transaction.Commit();
        });

    public void AddApplicationChanges(IEnumerable<ApplicationChangeRow> changes) =>
        Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            foreach (var change in changes)
            {
                Run(connection, "INSERT INTO app_changes VALUES ($ts, $area, $severity, $change)",
                    ("$ts", ToUnix(change.Utc)), ("$area", change.Area), ("$severity", change.Severity), ("$change", change.Change));
            }

            transaction.Commit();
        });

    /// <summary>Starts a problem, or updates the one already open for the same issue (keeping its worst severity).</summary>
    public void OpenIncident(AgentIssueDto issue, string source, DateTime utc) =>
        Write(connection =>
        {
            var updated = Run(connection, """
                UPDATE incidents SET title = $title, message = $message,
                    severity = CASE WHEN severity = 'Critical' THEN severity ELSE $severity END
                WHERE issue_id = $id AND ended IS NULL
                """, ("$id", issue.Id), ("$title", issue.Title), ("$message", issue.Message), ("$severity", issue.Severity));
            if (updated == 0)
            {
                Run(connection, """
                    INSERT INTO incidents (issue_id, source, severity, title, message, started)
                    VALUES ($id, $source, $severity, $title, $message, $ts)
                    """, ("$id", issue.Id), ("$source", source), ("$severity", issue.Severity), ("$title", issue.Title),
                    ("$message", issue.Message), ("$ts", ToUnix(utc)));
            }
        });

    public void CloseIncident(string issueId, DateTime utc) =>
        Write(connection => Run(connection, "UPDATE incidents SET ended = $ts, ended_by = 'resolved' WHERE issue_id = $id AND ended IS NULL",
            ("$id", issueId), ("$ts", ToUnix(utc))));

    /// <summary>Deletes every row older than <paramref name="utc"/>; returns how many were deleted.</summary>
    public int RemoveOlderThan(DateTime utc)
    {
        var removed = 0;
        Write(connection =>
        {
            var ts = ToUnix(utc);
            foreach (var table in new[] { "samples", "disk_samples", "point_samples", "process_samples", "speed_tests", "settings_changes", "app_changes" })
            {
                removed += Run(connection, $"DELETE FROM {table} WHERE ts < $ts", ("$ts", ts));
            }

            removed += Run(connection, "DELETE FROM incidents WHERE ended IS NOT NULL AND ended < $ts", ("$ts", ts));
        });
        return removed;
    }

    /// <summary>Gives the space of deleted rows back to the disk.</summary>
    public void Compact() =>
        Write(connection =>
        {
            Run(connection, "VACUUM");
            Run(connection, "PRAGMA wal_checkpoint(TRUNCATE)");
        });

    public List<SampleRow> GetSamples(DateTime fromUtc, DateTime toUtc) =>
        Read("""
            SELECT ts, cpu, ram, temp, internet, network, ping_ms, loss, wifi, rx_mb, tx_mb, disk_active, disk_read, disk_write, disk_ms
            FROM samples WHERE ts >= $from AND ts < $to ORDER BY ts
            """, fromUtc, toUtc,
            r => new SampleRow(FromUnix(r.GetInt64(0)), r.GetDouble(1), r.GetDouble(2), NullableDouble(r, 3), NullableBool(r, 4), NullableBool(r, 5),
                NullableDouble(r, 6), NullableDouble(r, 7), r.IsDBNull(8) ? null : r.GetInt32(8),
                NullableDouble(r, 9), NullableDouble(r, 10),
                NullableDouble(r, 11), NullableDouble(r, 12), NullableDouble(r, 13), NullableDouble(r, 14)));

    public List<DiskSampleRow> GetDiskSamples(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, drive, total_gb, free_gb FROM disk_samples WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new DiskSampleRow(FromUnix(r.GetInt64(0)), r.GetString(1), r.GetDouble(2), r.GetDouble(3)));

    public List<PointSampleRow> GetPointSamples(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, point_id, name, type, status, response_ms FROM point_samples WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new PointSampleRow(FromUnix(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), NullableDouble(r, 5)));

    public List<ProcessSampleRow> GetProcessSamples(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, kind, name, value FROM process_samples WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new ProcessSampleRow(FromUnix(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetDouble(3)));

    public List<SpeedTestRow> GetSpeedTests(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, download, upload, error FROM speed_tests WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new SpeedTestRow(FromUnix(r.GetInt64(0)), NullableDouble(r, 1), NullableDouble(r, 2), r.IsDBNull(3) ? null : r.GetString(3)));

    /// <summary>Problems that were open at any moment of the period.</summary>
    public List<IncidentRow> GetIncidents(DateTime fromUtc, DateTime toUtc) =>
        Read("""
            SELECT id, issue_id, source, severity, title, message, started, ended, ended_by FROM incidents
            WHERE started < $to AND (ended IS NULL OR ended >= $from) ORDER BY started
            """, fromUtc, toUtc,
            r => new IncidentRow(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? string.Empty : r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? string.Empty : r.GetString(5), FromUnix(r.GetInt64(6)),
                r.IsDBNull(7) ? null : FromUnix(r.GetInt64(7)), r.IsDBNull(8) ? null : r.GetString(8)));

    public List<SettingsChangeRow> GetSettingsChanges(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, change FROM settings_changes WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new SettingsChangeRow(FromUnix(r.GetInt64(0)), r.GetString(1)));

    public List<ApplicationChangeRow> GetApplicationChanges(DateTime fromUtc, DateTime toUtc) =>
        Read("SELECT ts, area, severity, change FROM app_changes WHERE ts >= $from AND ts < $to ORDER BY ts", fromUtc, toUtc,
            r => new ApplicationChangeRow(FromUnix(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetString(3)));

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name";
        check.Parameters.AddWithValue("$name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) == 0)
        {
            Run(connection, $"ALTER TABLE {table} ADD COLUMN {column} REAL");
        }
    }

    private List<T> Read<T>(string sql, DateTime fromUtc, DateTime toUtc, Func<SqliteDataReader, T> map)
    {
        var rows = new List<T>();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$from", ToUnix(fromUtc));
            command.Parameters.AddWithValue("$to", ToUnix(toUtc));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(map(reader));
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "[Reports] Reading the reports database failed");
        }

        return rows;
    }

    private void Write(Action<SqliteConnection> write)
    {
        try
        {
            lock (_writeLock)
            {
                using var connection = Open();
                write(connection);
                foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
                    if (File.Exists(path)) MonitorAgent.Shared.Security.PrivateFile.Secure(path);
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "[Reports] Writing to the reports database failed");
        }
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters) =>
        Write(connection => Run(connection, sql, parameters));

    private static int Run(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static long ToUnix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

    private static int? ToInt(bool? value) => value is null ? null : value.Value ? 1 : 0;

    private static double? NullableDouble(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDouble(index);

    private static bool? NullableBool(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index) != 0;
}
