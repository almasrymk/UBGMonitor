using Google.Protobuf;
using Microsoft.Data.Sqlite;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

public sealed record OutboxRow(long Sequence, string Kind, AgentMessage Message);

/// <summary>
/// Guaranteed messages waiting for the cloud's <c>Ack</c> (AG-5): one SQLite table, the row number is the protocol
/// sequence (strictly increasing). Rows are deleted when acknowledged. Above the size cap the oldest metric rows are
/// dropped first; issue, config and command rows are never dropped.
/// </summary>
public sealed class Outbox : IDisposable
{
    public const string Metric = "metric";
    public const string Issue = "issue";
    public const string Inventory = "inventory";
    public const string Points = "points";
    public const string Config = "config";
    public const string Command = "command";

    private readonly SqliteConnection _connection;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public Outbox(string path, long maxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        _maxBytes = maxBytes;
        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, message_id TEXT NOT NULL, kind TEXT NOT NULL, payload BLOB NOT NULL,
                created_utc INTEGER NOT NULL, sent_utc INTEGER NULL);
            CREATE TABLE IF NOT EXISTS cloud_state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
        LastAcknowledged = long.TryParse(GetState("acked"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var acked) ? acked : 0;
    }

    /// <summary>Stores a guaranteed message; returns its sequence.</summary>
    public long Enqueue(string kind, AgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            if (string.IsNullOrEmpty(message.MessageId))
                message.MessageId = Guid.NewGuid().ToString("N");
            message.Sequence = 0;
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO outbox (message_id, kind, payload, created_utc) VALUES ($id, $kind, $payload, $now); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$id", message.MessageId);
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$payload", message.ToByteArray());
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var sequence = (long)command.ExecuteScalar()!;
            EnforceCap();
            return sequence;
        }
    }

    /// <summary>The waiting messages after <paramref name="afterSequence"/>, oldest first, with their sequence set.</summary>
    public IReadOnlyList<OutboxRow> Pending(long afterSequence, int limit = 100)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT seq, kind, payload FROM outbox WHERE seq > $after ORDER BY seq LIMIT $limit";
            command.Parameters.AddWithValue("$after", afterSequence);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var rows = new List<OutboxRow>();
            while (reader.Read())
            {
                var message = AgentMessage.Parser.ParseFrom((byte[])reader[2]);
                message.Sequence = (ulong)reader.GetInt64(0);
                rows.Add(new OutboxRow(reader.GetInt64(0), reader.GetString(1), message));
            }

            return rows;
        }
    }

    /// <summary>Everything up to and including <paramref name="sequence"/> is durable in the cloud.</summary>
    public int Acknowledge(long sequence)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM outbox WHERE seq <= $seq";
            command.Parameters.AddWithValue("$seq", sequence);
            var deleted = command.ExecuteNonQuery();
            if (sequence > LastAcknowledged)
            {
                LastAcknowledged = sequence;
                SetState("acked", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return deleted;
        }
    }

    /// <summary>The highest sequence the cloud acknowledged (sent in <c>Hello.last_acked_sequence</c>).</summary>
    public long LastAcknowledged { get; private set; }

    public int Depth
    {
        get
        {
            lock (_gate)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM outbox";
                return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return TotalBytes();
            }
        }
    }

    /// <summary>A small key/value store for the connector (issues already reported, inventory hashes).</summary>
    public string? GetState(string key)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT value FROM cloud_state WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetState(string key, string? value)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = value is null
                ? "DELETE FROM cloud_state WHERE key = $key"
                : "INSERT INTO cloud_state (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            command.Parameters.AddWithValue("$key", key);
            if (value is not null)
                command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();

    private void EnforceCap()
    {
        var total = TotalBytes();
        while (total > _maxBytes)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM outbox WHERE seq IN (SELECT seq FROM outbox WHERE kind = $kind ORDER BY seq LIMIT 50)";
            command.Parameters.AddWithValue("$kind", Metric);
            if (command.ExecuteNonQuery() == 0)
                return;
            total = TotalBytes();
        }
    }

    private long TotalBytes()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(length(payload)), 0) FROM outbox";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
