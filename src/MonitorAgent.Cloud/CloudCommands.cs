using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>
/// The agent's checks of a remote action (05 section 9, AG-13), in this order: the local switch
/// (<c>Cloud:AllowRemoteActions</c>), a known key, the ES256 signature (P1363) over
/// <c>command_id|type|parameters_json|expires_at|nonce|device_id</c> with the expiry as Unix milliseconds and this
/// device's own id, not expired and at most 5 minutes ahead, and a nonce not seen in the last 10 minutes.
/// </summary>
public sealed class CommandVerifier
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NonceMemory = TimeSpan.FromMinutes(10);

    /// <summary>Allowed clock difference between the cloud and the device.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);

    private const string NonceStateKey = "command-nonces";
    private readonly Guid _deviceId;
    private readonly TimeProvider _clock;
    private readonly bool _allow;
    private readonly Outbox? _store;
    private readonly Dictionary<string, ECDsa> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nonces;
    private readonly object _gate = new();

    /// <param name="jwks">The <c>commandSigningKeys</c> set received at enrollment (null: every command is refused).</param>
    /// <param name="store">Keeps the seen nonces across restarts (the outbox database); null keeps them in memory only.</param>
    public CommandVerifier(Guid deviceId, string? jwks, bool allowRemoteActions, TimeProvider clock, Outbox? store = null)
    {
        _deviceId = deviceId;
        _clock = clock;
        _allow = allowRemoteActions;
        _store = store;
        if (!string.IsNullOrWhiteSpace(jwks))
            LoadKeys(jwks);
        _nonces = LoadNonces();
    }

    public int KeyCount => _keys.Count;

    /// <summary>Null when the command may run; otherwise the refusal sent back as <c>REJECTED</c> or <c>EXPIRED</c>.</summary>
    public (CommandStatus Status, string Reason)? Check(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_allow)
            return (CommandStatus.Rejected, "Remote actions are turned off on this device.");
        if (!_keys.TryGetValue(command.KeyId, out var key))
            return (CommandStatus.Rejected, "Unknown signing key.");
        if (!Guid.TryParseExact(command.CommandId, "N", out var commandId) || command.ExpiresAt is null)
            return (CommandStatus.Rejected, "Malformed command.");
        var expiresAt = command.ExpiresAt.ToDateTimeOffset();
        var payload = Payload(commandId, command.Type, command.ParametersJson, expiresAt, command.Nonce, _deviceId);
        // The device id is part of the signed text: a command signed for another device fails here.
        if (!key.VerifyData(Encoding.UTF8.GetBytes(payload), command.Signature.ToByteArray(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return (CommandStatus.Rejected, "Invalid signature.");
        var now = _clock.GetUtcNow();
        if (expiresAt <= now)
            return (CommandStatus.Expired, "The command has expired.");
        if (expiresAt > now + MaxLifetime + Skew)
            return (CommandStatus.Rejected, "The expiry is more than 5 minutes ahead.");
        lock (_gate)
        {
            foreach (var old in _nonces.Where(n => now - n.Value > NonceMemory).Select(n => n.Key).ToList())
                _nonces.Remove(old);
            if (!_nonces.TryAdd(command.Nonce, now))
                return (CommandStatus.Rejected, "Replayed nonce.");
            _store?.SetState(NonceStateKey, JsonSerializer.Serialize(_nonces.ToDictionary(n => n.Key, n => n.Value.ToUnixTimeMilliseconds())));
        }

        return null;
    }

    /// <summary>The canonical signed text (05 section 9).</summary>
    public static string Payload(Guid commandId, string type, string parametersJson, DateTimeOffset expiresAt, string nonce, Guid deviceId) =>
        string.Join('|', commandId.ToString("N"), type, parametersJson, expiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), nonce, deviceId.ToString("N"));

    private void LoadKeys(string jwks)
    {
        using var document = JsonDocument.Parse(jwks);
        if (!document.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            return;
        foreach (var key in keys.EnumerateArray())
        {
            if (key.TryGetProperty("kty", out var kty) && kty.GetString() == "EC" && key.TryGetProperty("crv", out var crv) && crv.GetString() == "P-256"
                && key.TryGetProperty("kid", out var kid) && key.TryGetProperty("x", out var x) && key.TryGetProperty("y", out var y))
            {
                _keys[kid.GetString()!] = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = FromBase64Url(x.GetString()!), Y = FromBase64Url(y.GetString()!) },
                });
            }
        }
    }

    private Dictionary<string, DateTimeOffset> LoadNonces()
    {
        var nonces = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (_store?.GetState(NonceStateKey) is not { Length: > 0 } json)
            return nonces;
        try
        {
            foreach (var (nonce, at) in JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [])
                nonces[nonce] = DateTimeOffset.FromUnixTimeMilliseconds(at);
        }
        catch (JsonException)
        {
            // A damaged entry is dropped; commands still expire within 5 minutes.
        }

        return nonces;
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}

/// <summary>Runs a verified remote action. The service implements it over its own collectors and the OS service manager.</summary>
public interface ICloudCommandExecutor
{
    /// <summary><paramref name="service"/> is set for the three service commands (already validated as a plain service name).</summary>
    Task<(bool Success, string Output)> ExecuteAsync(string type, string? service, CancellationToken cancellationToken);
}

/// <summary>Without a host executor only <c>refresh-inventory</c> runs (the connector handles it); the rest fail politely.</summary>
internal sealed class InventoryOnlyCommandExecutor : ICloudCommandExecutor
{
    public Task<(bool Success, string Output)> ExecuteAsync(string type, string? service, CancellationToken cancellationToken) =>
        Task.FromResult((false, $"'{type}' is not supported by this agent host."));
}

/// <summary>Verifies a <c>Command</c>, runs it and queues the guaranteed <c>CommandResult</c> (one per command).</summary>
public sealed partial class CommandRunner(CommandVerifier verifier, ICloudCommandExecutor executor, Outbox outbox, Action requestInventory, TimeProvider clock)
{
    public const int MaxOutputLength = 64 * 1024;

    /// <summary>The six command types of 05 section 9.</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "refresh-inventory", "run-speed-test", "restart-agent", "service-start", "service-stop", "service-restart",
    };

    public async Task<CommandResult> RunAsync(Command command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (status, output) = verifier.Check(command) is { } refusal ? (refusal.Status, refusal.Reason) : await ExecuteAsync(command, cancellationToken);
        var result = new CommandResult
        {
            CommandId = command.CommandId,
            Status = status,
            Output = output.Length <= MaxOutputLength ? output : output[..MaxOutputLength],
            CompletedAt = Timestamp.FromDateTimeOffset(clock.GetUtcNow()),
        };
        outbox.Enqueue(Outbox.Command, new AgentMessage { CommandResult = result });
        return result;
    }

    private async Task<(CommandStatus, string)> ExecuteAsync(Command command, CancellationToken cancellationToken)
    {
        if (!Types.Contains(command.Type))
            return (CommandStatus.Rejected, "Unknown command type.");
        string? service = null;
        if (command.Type.StartsWith("service-", StringComparison.Ordinal))
        {
            service = ServiceParameter(command.ParametersJson);
            if (service is null)
                return (CommandStatus.Rejected, "A service command needs a valid service name.");
        }

        if (command.Type == "refresh-inventory")
        {
            requestInventory();
            return (CommandStatus.Succeeded, "Inventory refresh queued.");
        }

        try
        {
            var (success, output) = await executor.ExecuteAsync(command.Type, service, cancellationToken);
            return (success ? CommandStatus.Succeeded : CommandStatus.Failed, output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (CommandStatus.Failed, ex.Message);
        }
    }

    /// <summary>The <c>service</c> parameter, only when it is a plain service name (no paths, spaces or shell characters).</summary>
    internal static string? ServiceParameter(string parametersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            return document.RootElement.TryGetProperty("service", out var s) && s.GetString() is { } name && ServiceName().IsMatch(name) ? name : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.@-]{1,128}$")]
    private static partial Regex ServiceName();
}
