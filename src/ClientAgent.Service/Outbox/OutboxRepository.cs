using System.Text.Json;
using ClientAgent.Shared.Models;
using ClientAgent.Service.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Outbox;

public interface IOutboxRepository
{
    Task InitializeDatabaseAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringEvent>> GetPendingAsync(int count, CancellationToken cancellationToken = default);

    Task MarkAsSentAsync(Guid eventId, string destination, CancellationToken cancellationToken = default);

    Task IncrementRetryAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task<long> GetSentCountSinceAsync(DateTime utcTimestamp, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}

public sealed class OutboxRepository : IOutboxRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _databasePath;
    private readonly ILogger<OutboxRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OutboxRepository(IOptions<OutboxOptions> options, ILogger<OutboxRepository> logger)
        : this(options.Value.DatabasePath, logger)
    {
    }

    public OutboxRepository(string databasePath, ILogger<OutboxRepository> logger)
    {
        _databasePath = databasePath;
        _logger = logger;
    }

    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Outbox (
                EventId TEXT PRIMARY KEY,
                MonitorPointId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Status TEXT NOT NULL,
                Payload TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SentAt TEXT NULL,
                Destination TEXT NULL,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                LastSentAt TEXT NULL,
                IsSent INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Outbox_Pending
                ON Outbox (IsSent, CreatedAt)
                WHERE IsSent = 0;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Outbox database initialized at {Path}", _databasePath);
    }

    public async Task WriteAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO Outbox
                    (EventId, MonitorPointId, EventType, Severity, Status, Payload, CreatedAt, IsSent, RetryCount)
                VALUES
                    ($eventId, $monitorPointId, $eventType, $severity, $status, $payload, $createdAt, 0, 0);
                """;
            command.Parameters.AddWithValue("$eventId", monitoringEvent.EventId.ToString());
            command.Parameters.AddWithValue("$monitorPointId", monitoringEvent.MonitorPointId);
            command.Parameters.AddWithValue("$eventType", monitoringEvent.EventType.ToString());
            command.Parameters.AddWithValue("$severity", monitoringEvent.Severity.ToString());
            command.Parameters.AddWithValue("$status", monitoringEvent.Status.ToString());
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(monitoringEvent, JsonOptions));
            command.Parameters.AddWithValue("$createdAt", monitoringEvent.TimestampUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MonitoringEvent>> GetPendingAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Payload FROM Outbox
            WHERE IsSent = 0
            ORDER BY CreatedAt
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var events = new List<MonitoringEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            var item = JsonSerializer.Deserialize<MonitoringEvent>(payload, JsonOptions);
            if (item is not null)
            {
                events.Add(item);
            }
        }

        return events;
    }

    public async Task MarkAsSentAsync(Guid eventId, string destination, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Outbox
            SET IsSent = 1,
                SentAt = $sentAt,
                LastSentAt = $sentAt,
                Destination = $destination
            WHERE EventId = $eventId;
            """;
        var now = DateTime.UtcNow.ToString("O");
        command.Parameters.AddWithValue("$sentAt", now);
        command.Parameters.AddWithValue("$destination", destination);
        command.Parameters.AddWithValue("$eventId", eventId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task IncrementRetryAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Outbox
            SET RetryCount = RetryCount + 1,
                LastSentAt = $lastSentAt
            WHERE EventId = $eventId;
            """;
        command.Parameters.AddWithValue("$lastSentAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$eventId", eventId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Outbox WHERE IsSent = 0;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : Convert.ToInt64(result);
    }

    public async Task<long> GetSentCountSinceAsync(DateTime utcTimestamp, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Outbox WHERE IsSent = 1 AND SentAt >= $start;";
        command.Parameters.AddWithValue("$start", utcTimestamp.ToString("O"));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? count : Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<MonitoringEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Payload FROM Outbox
            ORDER BY CreatedAt DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$count", count);

        var events = new List<MonitoringEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.GetString(0);
            var item = JsonSerializer.Deserialize<MonitoringEvent>(payload, JsonOptions);
            if (item is not null)
            {
                events.Add(item);
            }
        }

        return events;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
