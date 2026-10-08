using System.Text.Json;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>
/// The connector's message pump (AG-3). Producing never waits for the network: samples become minute aggregates, problems
/// become issue events and changed inventory becomes inventory updates, all in the outbox. The connection loop enrolls
/// when needed, gets a device token, keeps one gateway session open and reconnects with back-off (AG-10).
/// </summary>
public sealed partial class CloudAgentService(
    IOptions<CloudOptions> options, ICloudAgentSource source, ICloudLicenseSink license, CloudStateStore store, CloudHttpClient http, DeviceTokenProvider tokens,
    CloudStatus status, TimeProvider clock, ILogger<CloudAgentService> logger) : BackgroundService
{
    public static readonly TimeSpan IssueInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PointInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan InventoryInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan EnrollRetry = TimeSpan.FromMinutes(1);

    private readonly ReconnectPolicy _reconnect = new();
    private Outbox? _outbox;
    private volatile bool _inventoryWanted = true;
    private volatile bool _fullPointsWanted = true;

    /// <summary>The outbox, once the service has started (tests).</summary>
    public Outbox? Outbox => _outbox;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.BaseUrl))
        {
            status.Set("Disabled");
            return;
        }

        _outbox = new Outbox(Path.Combine(o.StateFolder, "cloud.db"), o.MaxOutboxBytes);
        try
        {
            await Task.WhenAll(ProduceAsync(_outbox, o, stoppingToken), ConnectLoopAsync(_outbox, o, stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            _outbox.Dispose();
        }
    }

    // ---------------------------------------------------------------- producing (works offline)

    private async Task ProduceAsync(Outbox outbox, CloudOptions o, CancellationToken cancellationToken)
    {
        var aggregator = new MinuteAggregator();
        var issues = new IssueChangeTracker(outbox);
        var inventory = new InventoryPublisher(outbox);
        var nextIssues = DateTimeOffset.MinValue;
        var nextPoints = DateTimeOffset.MinValue;
        var nextInventory = DateTimeOffset.MinValue;
        var pointHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? diskHash = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sample = await source.SampleAsync(cancellationToken);
                if (aggregator.Add(sample) is { } minute)
                {
                    var batch = new MetricBatch { Minutes = { minute } };
                    var disks = await source.DisksAsync(cancellationToken);
                    var hash = JsonSerializer.Serialize(disks);
                    if (minute.BucketStart.ToDateTimeOffset().Minute == 0 || hash != diskHash)
                    {
                        batch.Disks.AddRange(disks.Select(d => new DiskUsage { Drive = d.Drive, Label = d.Label, FileSystem = d.FileSystem, TotalGb = d.TotalGb, UsedGb = d.UsedGb, FreeGb = d.FreeGb }));
                        diskHash = hash;
                    }

                    outbox.Enqueue(Outbox.Metric, new AgentMessage { MetricBatch = batch });
                }

                var now = clock.GetUtcNow();
                if (now >= nextIssues)
                {
                    nextIssues = now.Add(IssueInterval);
                    foreach (var change in issues.Changes(source.Issues(), now))
                        outbox.Enqueue(Outbox.Issue, new AgentMessage { Issue = change });
                }

                if (now >= nextPoints || _fullPointsWanted)
                {
                    nextPoints = now.Add(PointInterval);
                    QueuePoints(outbox, source.MonitorPoints(), pointHashes, _fullPointsWanted);
                    _fullPointsWanted = false;
                }

                if (now >= nextInventory || _inventoryWanted)
                {
                    nextInventory = now.Add(InventoryInterval);
                    _inventoryWanted = false;
                    inventory.Publish(await source.InventoryAsync(cancellationToken));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogProduceFailed(logger, ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(o.SampleSeconds), clock, cancellationToken);
        }
    }

    internal static void QueuePoints(Outbox outbox, IReadOnlyList<CloudPoint> points, Dictionary<string, string> hashes, bool full)
    {
        var changed = new List<MonitorPointStatus>();
        foreach (var p in points)
        {
            var hash = JsonSerializer.Serialize(p with { LastChecked = null });
            if (!full && hashes.TryGetValue(p.Key, out var previous) && previous == hash)
                continue;
            hashes[p.Key] = hash;
            var status = new MonitorPointStatus
            {
                Key = p.Key, DisplayName = p.DisplayName, Type = p.Type, Target = p.Target, Enabled = p.Enabled, Status = PointStatusOf(p.Status), Message = p.Message ?? string.Empty,
                IntervalSeconds = (uint)Math.Max(0, p.IntervalSeconds),
            };
            if (p.ResponseMs is { } ms)
                status.ResponseMs = ms;
            if (p.LastChecked is { } checkedAt)
                status.LastChecked = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(checkedAt);
            if (p.StatusSince is { } since)
                status.StatusSince = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(since);
            changed.Add(status);
        }

        if (full || changed.Count > 0)
            outbox.Enqueue(Outbox.Points, new AgentMessage { MonitorPoints = new MonitorPointReport { Full = full, Points = { changed } } });
    }

    internal static PointStatus PointStatusOf(string status) => status.ToLowerInvariant() switch
    {
        "healthy" or "up" or "ok" => PointStatus.PointHealthy,
        "warning" => PointStatus.PointWarning,
        "critical" or "problem" or "down" => PointStatus.PointCritical,
        _ => PointStatus.PointUnknown,
    };

    // ---------------------------------------------------------------- connecting

    private async Task ConnectLoopAsync(Outbox outbox, CloudOptions o, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = await ConnectOnceAsync(outbox, o, cancellationToken);
            if (delay is { } wait)
                await Task.Delay(wait, clock, cancellationToken);
        }
    }

    /// <summary>One attempt: enroll if needed, get a token, run a session. Returns how long to wait before the next one.</summary>
    internal async Task<TimeSpan?> ConnectOnceAsync(Outbox outbox, CloudOptions o, CancellationToken cancellationToken)
    {
        var state = store.Load();
        if (!state.IsEnrolled)
        {
            if (string.IsNullOrWhiteSpace(o.ProductKey))
            {
                status.Set("NotEnrolled", "No product key: set Cloud:ProductKey or MONITORAGENT_PRODUCTKEY, or activate from the app.");
                return EnrollRetry;
            }

            try
            {
                status.Set("Enrolling");
                var result = await http.EnrollAsync(o.ProductKey, source.Fingerprint, source.Host(), o.LocationCode, cancellationToken);
                state = CloudStateStore.Enrolled(state, result);
                store.Save(state);
                license.Update(result.LicenseState ?? "Licensed", null, result.LicenseToken, result.LicenseCheckAfter);
                LogEnrolled(logger, result.DeviceId, result.TenantName, result.LocationName);
            }
            catch (CloudRequestException ex)
            {
                status.Set("EnrollFailed", ex.Code);
                LogEnrollFailed(logger, ex.Code, ex.Message);
                return ex.RetryAfter ?? (ex.Code.StartsWith("LIC_", StringComparison.Ordinal) ? TimeSpan.FromMinutes(15) : _reconnect.Next());
            }
            catch (HttpRequestException ex)
            {
                status.Set("Offline", ex.Message);
                return _reconnect.Next();
            }
        }

        string token;
        try
        {
            token = await tokens.GetAsync(state.DeviceId, CloudStateStore.Secret(state) ?? string.Empty, cancellationToken);
        }
        catch (CloudRequestException ex) when (ex.CredentialInvalid)
        {
            // Retired or revoked: enroll again with the same fingerprint (the cloud re-activates the device).
            LogCredentialInvalid(logger, state.DeviceId);
            store.Save(state with { ProtectedDeviceSecret = null });
            tokens.Forget();
            status.Set("NotEnrolled", ex.Code);
            return _reconnect.Next();
        }
        catch (CloudRequestException ex)
        {
            status.Set("Offline", ex.Code);
            return ex.RetryAfter ?? _reconnect.Next();
        }
        catch (HttpRequestException ex)
        {
            status.Set("Offline", ex.Message);
            return _reconnect.Next();
        }

        var gateway = !string.IsNullOrWhiteSpace(o.GatewayUrl) ? o.GatewayUrl! : !string.IsNullOrWhiteSpace(state.GatewayUrl) ? state.GatewayUrl! : o.BaseUrl!;
        status.Set("Connecting");
        using var channel = GrpcChannel.ForAddress(gateway, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true,
                UseProxy = true,
            },
        });
        var session = new GatewaySession(channel, token, outbox, source, license, status, logger, clock);
        session.Opened += () => _fullPointsWanted = true;
        session.InventoryRequested += () => _inventoryWanted = true;
        try
        {
            await session.RunAsync(GoodbyeReason.ServiceStopping, cancellationToken);
        }
        catch (Exception ex) when (ex is Grpc.Core.RpcException or HttpRequestException or IOException or InvalidOperationException)
        {
            status.Set("Offline", ex.Message);
            LogStreamFailed(logger, ex.Message);
        }

        if (cancellationToken.IsCancellationRequested)
            return null;
        if (session.Welcome is not null)
            _reconnect.Reset();
        if (session.Disconnected is { } disconnect)
        {
            status.Set("Disconnected", disconnect.Code);
            if (disconnect.Code is "CREDENTIAL_REVOKED" or "DEVICE_RETIRED")
            {
                tokens.Forget();
                return disconnect.RetryAfterSeconds > 0 ? TimeSpan.FromSeconds(disconnect.RetryAfterSeconds) : TimeSpan.FromMinutes(10);
            }

            if (disconnect.RetryAfterSeconds == 0)
                return TimeSpan.FromMinutes(10);
            return _reconnect.Next(disconnect.RetryAfterSeconds);
        }

        if (status.State == "Connected")
            status.Set("Offline", "The stream closed.");
        return _reconnect.Next();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[Cloud] Enrolled as device {DeviceId} ({Tenant} / {Location})")]
    private static partial void LogEnrolled(ILogger logger, Guid deviceId, string? tenant, string? location);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Cloud] Enrollment refused: {Code} ({Message})")]
    private static partial void LogEnrollFailed(ILogger logger, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Cloud] The credential of device {DeviceId} is no longer valid; enrolling again")]
    private static partial void LogCredentialInvalid(ILogger logger, Guid deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Cloud] Stream ended: {Message}")]
    private static partial void LogStreamFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Cloud] Collecting for the cloud failed: {Message}")]
    private static partial void LogProduceFailed(ILogger logger, string message);
}

public static class CloudServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connector. The host must also register an <see cref="ICloudAgentSource"/>; an
    /// <see cref="ICloudLicenseSink"/> is optional.
    /// </summary>
    public static IServiceCollection AddMonitorCloud(this IServiceCollection services, CloudOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICloudLicenseSink, NoLicenseSink>();
        services.AddSingleton(new CloudStateStore(options.StateFolder));
        services.AddSingleton<CloudStatus>();
        services.AddHttpClient<CloudHttpClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<DeviceTokenProvider>();
        services.AddSingleton<CloudAgentService>();
        services.AddHostedService(sp => sp.GetRequiredService<CloudAgentService>());
        return services;
    }
}
