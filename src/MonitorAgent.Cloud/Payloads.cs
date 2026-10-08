using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>JSON documents for <c>Snapshot</c>, <c>InventoryUpdate</c> and <c>ConfigUpdate</c>: Brotli-compressed UTF-8.</summary>
public static class Payloads
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ByteString Compress(string json)
    {
        using var buffer = new MemoryStream();
        using (var brotli = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            brotli.Write(bytes, 0, bytes.Length);
        }

        return ByteString.CopyFrom(buffer.ToArray());
    }

    public static string Decompress(ByteString data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var input = new MemoryStream(data.ToByteArray());
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(brotli, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string Sha256(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    /// <summary>Kind names of the inventory documents (05 section 5).</summary>
    public static bool TryKind(string name, out InventoryKind kind)
    {
        kind = name.ToLowerInvariant() switch
        {
            "hardware" => InventoryKind.Hardware,
            "os" => InventoryKind.Os,
            "network" => InventoryKind.Network,
            "disks" => InventoryKind.Disks,
            "programs" => InventoryKind.Programs,
            "services" => InventoryKind.Services,
            "users" => InventoryKind.Users,
            "sensors" => InventoryKind.Sensors,
            _ => InventoryKind.Unspecified,
        };
        return kind != InventoryKind.Unspecified;
    }
}

/// <summary>Queues an <c>InventoryUpdate</c> for each document whose hash changed since it was last sent.</summary>
public sealed class InventoryPublisher(Outbox outbox)
{
    public int Publish(IReadOnlyDictionary<string, object> documents, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var queued = 0;
        foreach (var (name, document) in documents)
        {
            if (!Payloads.TryKind(name, out var kind))
                continue;
            var json = JsonSerializer.Serialize(document, Payloads.Json);
            var hash = Payloads.Sha256(json);
            var key = "inventory:" + kind;
            if (!force && outbox.GetState(key) == hash)
                continue;
            outbox.Enqueue(Outbox.Inventory, new AgentMessage { Inventory = new InventoryUpdate { Kind = kind, Hash = hash, JsonBrotli = Payloads.Compress(json) } });
            outbox.SetState(key, hash);
            queued++;
        }

        return queued;
    }
}

/// <summary>Exponential back-off 1 s -> 5 min with +/-20 % jitter (AG-10); <c>retry_after_seconds</c> wins when given.</summary>
public sealed class ReconnectPolicy(Random? random = null)
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);
    private readonly Random _random = random ?? Random.Shared;
    private TimeSpan _next = First;

    public TimeSpan Next(uint? retryAfterSeconds = null)
    {
        if (retryAfterSeconds is > 0)
        {
            _next = First;
            return TimeSpan.FromSeconds(retryAfterSeconds.Value);
        }

        var delay = _next * (0.8 + (_random.NextDouble() * 0.4));
        _next = TimeSpan.FromTicks(Math.Min(Max.Ticks, _next.Ticks * 2));
        return delay;
    }

    public void Reset() => _next = First;
}
