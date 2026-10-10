using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>
/// One gRPC stream to the gateway (05 section 2): Hello, Welcome, resend of everything above
/// <c>last_received_sequence</c>, then the steady state (heartbeats, guaranteed messages from the outbox, snapshots,
/// live samples while the device screen is open). Ends on a <c>Disconnect</c>, a broken stream or cancellation;
/// cancellation sends <c>Goodbye</c> first.
/// </summary>
public sealed partial class GatewaySession(
    GrpcChannel channel, string token, Outbox outbox, ICloudAgentSource source, ICloudLicenseSink license, ICloudConfigApplier configs, CloudStatus status, ILogger logger,
    TimeProvider clock, CommandRunner? commands = null)
{
    public static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _write = new(1, 1);
    private IAsyncStreamWriter<AgentMessage>? _writer;
    private long _lastSent;
    private DateTimeOffset _liveUntil = DateTimeOffset.MinValue;
    private TimeSpan _liveInterval = TimeSpan.FromSeconds(2);

    public Welcome? Welcome { get; private set; }

    public Disconnect? Disconnected { get; private set; }

    /// <summary>Raised once the stream is open (after <c>Welcome</c>): the pump queues the full monitor point report.</summary>
    public event Action? Opened;

    /// <summary>The cloud asked for inventory (<c>RequestInventory</c>).</summary>
    public event Action? InventoryRequested;

    public bool Live => clock.GetUtcNow() < _liveUntil;

    public async Task RunAsync(GoodbyeReason goodbyeOnCancel, CancellationToken cancellationToken)
    {
        var client = new AgentGateway.AgentGatewayClient(channel);
        using var streamCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var call = client.Connect(new Metadata { { "Authorization", $"Bearer {token}" } }, cancellationToken: streamCancel.Token);
        _writer = call.RequestStream;
        var host = source.Host();
        await SendAsync(new AgentMessage
        {
            Hello = new Hello
            {
                ProtocolVersion = 1, AgentVersion = host.AgentVersion, Hostname = host.Hostname, OsFamily = Family(host.OsFamily), OsName = host.OsName,
                OsVersion = host.OsVersion, Architecture = host.Architecture, LocalIp = host.LocalIp ?? string.Empty, MacAddress = host.MacAddress ?? string.Empty,
                LastAckedSequence = (ulong)Math.Max(0, outbox.LastAcknowledged),
                BootTime = host.BootTime is { } boot ? Timestamp.FromDateTimeOffset(boot) : null,
            },
        }, cancellationToken);

        using (var welcomeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            welcomeTimeout.CancelAfter(WelcomeTimeout);
            if (!await call.ResponseStream.MoveNext(welcomeTimeout.Token))
                return;
            var first = call.ResponseStream.Current;
            if (first.BodyCase == CloudMessage.BodyOneofCase.Disconnect)
            {
                Disconnected = first.Disconnect;
                return;
            }

            if (first.BodyCase != CloudMessage.BodyOneofCase.Welcome)
                return;
            Welcome = first.Welcome;
        }

        var received = (long)Welcome.LastReceivedSequence;
        outbox.Acknowledge(received);
        _lastSent = received;
        status.Connected(Welcome.SessionId);
        LogConnected(logger, Welcome.SessionId, outbox.Depth);
        license.Update(Welcome.LicenseState == LicenseState.Unlicensed ? "Unlicensed" : "Licensed", null, null, null);
        Opened?.Invoke();

        var receive = ReceiveAsync(call.ResponseStream, streamCancel.Token);
        var heartbeat = TimeSpan.FromSeconds(Math.Clamp(Welcome.HeartbeatSeconds == 0 ? 30 : Welcome.HeartbeatSeconds, 5, 300));
        var nextHeartbeat = clock.GetUtcNow().Add(heartbeat);
        var nextSnapshot = clock.GetUtcNow();
        var nextLive = DateTimeOffset.MinValue;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !receive.IsCompleted)
            {
                await SendPendingAsync(cancellationToken);
                var now = clock.GetUtcNow();
                if (now >= nextHeartbeat)
                {
                    nextHeartbeat = now.Add(heartbeat);
                    await SendAsync(new AgentMessage { Heartbeat = new Heartbeat { UptimeSeconds = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds, OutboxDepth = (uint)outbox.Depth } }, cancellationToken);
                }

                if (now >= nextSnapshot)
                {
                    nextSnapshot = now.Add(SnapshotInterval);
                    await SendSnapshotAsync(cancellationToken);
                }

                if (Live && now >= nextLive)
                {
                    nextLive = now.Add(_liveInterval);
                    await SendLiveSampleAsync(cancellationToken);
                }

                await Task.WhenAny(receive, Task.Delay(Live ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(1), cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping: say Goodbye below.
        }

        if (cancellationToken.IsCancellationRequested && !receive.IsCompleted)
        {
            try
            {
                using var goodbye = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await SendAsync(new AgentMessage { Goodbye = new Goodbye { Reason = goodbyeOnCancel } }, goodbye.Token);
                await call.RequestStream.CompleteAsync();
            }
            catch (Exception ex) when (ex is RpcException or InvalidOperationException or OperationCanceledException)
            {
                // The stream is already gone.
            }
        }

        await streamCancel.CancelAsync();
        try
        {
            await receive;
        }
        catch (Exception ex) when (ex is RpcException or OperationCanceledException or InvalidOperationException)
        {
            // Closed.
        }
    }

    private async Task ReceiveAsync(IAsyncStreamReader<CloudMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var message = stream.Current;
            switch (message.BodyCase)
            {
                case CloudMessage.BodyOneofCase.Ack:
                    outbox.Acknowledge((long)message.Ack.Sequence);
                    status.Acknowledged((long)message.Ack.Sequence);
                    break;
                case CloudMessage.BodyOneofCase.SetMode:
                    var mode = message.SetMode;
                    if (mode.Mode == TelemetryMode.Live)
                    {
                        _liveInterval = TimeSpan.FromSeconds(Math.Clamp(mode.LiveIntervalSeconds == 0 ? 2 : mode.LiveIntervalSeconds, 2, 5));
                        _liveUntil = clock.GetUtcNow().AddSeconds(mode.TtlSeconds == 0 ? 60 : mode.TtlSeconds);
                    }
                    else
                    {
                        _liveUntil = DateTimeOffset.MinValue;
                    }

                    break;
                case CloudMessage.BodyOneofCase.LicenseUpdate:
                    var licence = message.LicenseUpdate;
                    license.Update(licence.State == LicenseState.Unlicensed ? "Unlicensed" : "Licensed", NullIfEmpty(licence.ReasonCode), NullIfEmpty(licence.Token),
                        licence.CheckAfter?.ToDateTimeOffset());
                    break;
                case CloudMessage.BodyOneofCase.ConfigUpdate:
                    var update = message.ConfigUpdate;
                    var (success, error) = await configs.ApplyAsync(update.Version, Payloads.Decompress(update.JsonBrotli), cancellationToken);
                    LogConfig(logger, update.Version, success, error ?? string.Empty);
                    outbox.Enqueue(Outbox.Config, new AgentMessage { ConfigApplied = new ConfigApplied { Version = update.Version, Success = success, Error = error ?? string.Empty } });
                    break;
                case CloudMessage.BodyOneofCase.Command:
                    // Runs beside the stream (a service restart can take a while); the result goes through the outbox.
                    var command = message.Command;
                    if (commands is null)
                        outbox.Enqueue(Outbox.Command, new AgentMessage { CommandResult = new CommandResult { CommandId = command.CommandId, Status = CommandStatus.Rejected, Output = "Remote actions are not available on this agent.", CompletedAt = Timestamp.FromDateTimeOffset(clock.GetUtcNow()) } });
                    else
                        _ = Task.Run(async () =>
                        {
                            var result = await commands.RunAsync(command, CancellationToken.None);
                            LogCommand(logger, command.Type, command.CommandId, result.Status);
                        }, CancellationToken.None);
                    break;
                case CloudMessage.BodyOneofCase.RequestInventory:
                    InventoryRequested?.Invoke();
                    break;
                case CloudMessage.BodyOneofCase.Disconnect:
                    Disconnected = message.Disconnect;
                    LogDisconnected(logger, message.Disconnect.Code, message.Disconnect.RetryAfterSeconds);
                    return;
            }
        }
    }

    private async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        foreach (var row in outbox.Pending(_lastSent))
        {
            await SendAsync(row.Message, cancellationToken);
            _lastSent = row.Sequence;
        }
    }

    private async Task SendSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await source.SnapshotAsync(cancellationToken) is { } document)
                await SendAsync(new AgentMessage { Snapshot = new Snapshot { JsonBrotli = Payloads.Compress(System.Text.Json.JsonSerializer.Serialize(document, Payloads.Json)) } }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RpcException)
        {
            LogSnapshotFailed(logger, ex.Message);
        }
    }

    private async Task SendLiveSampleAsync(CancellationToken cancellationToken)
    {
        var sample = await source.SampleAsync(cancellationToken);
        var live = new LiveSample
        {
            CpuPercent = sample.Cpu, RamPercent = sample.Ram, DiskActivePercent = sample.DiskActive ?? 0,
            NetRxBps = (ulong)Math.Max(0, sample.RxBps ?? 0), NetTxBps = (ulong)Math.Max(0, sample.TxBps ?? 0),
        };
        if (sample.TempC is { } temp)
            live.CpuTempC = temp;
        await SendAsync(new AgentMessage { LiveSample = live }, cancellationToken);
    }

    private async Task SendAsync(AgentMessage message, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("The stream is not open.");
        if (string.IsNullOrEmpty(message.MessageId))
            message.MessageId = Guid.NewGuid().ToString("N");
        message.SentAt = Timestamp.FromDateTimeOffset(clock.GetUtcNow());
        await _write.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteAsync(message, cancellationToken);
        }
        finally
        {
            _write.Release();
        }
    }

    internal static OsFamily Family(string os) => os.ToLowerInvariant() switch
    {
        "windows" => OsFamily.Windows,
        "linux" => OsFamily.Linux,
        "macos" or "osx" => OsFamily.Macos,
        _ => OsFamily.Other,
    };

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    [LoggerMessage(Level = LogLevel.Information, Message = "[Cloud] Connected (session {SessionId}), {Depth} messages waiting")]
    private static partial void LogConnected(ILogger logger, string sessionId, int depth);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Cloud] Disconnected by the cloud: {Code}, retry after {Seconds} s")]
    private static partial void LogDisconnected(ILogger logger, string code, uint seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Cloud] Configuration version {Version}: applied={Success} {Error}")]
    private static partial void LogConfig(ILogger logger, int version, bool success, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Cloud] Remote action {Type} ({CommandId}): {Status}")]
    private static partial void LogCommand(ILogger logger, string type, string commandId, CommandStatus status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Cloud] Snapshot not sent: {Message}")]
    private static partial void LogSnapshotFailed(ILogger logger, string message);
}
