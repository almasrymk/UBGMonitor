using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class MadkhalMonitor : BackgroundService, IMonitoringModule
{
    private readonly HttpClient _httpClient;
    private readonly IEventPublisher _publisher;
    private readonly IConnectivityTracker _connectivity;
    private readonly IAgentIdentity _identity;
    private readonly RoutingOptions _routing;
    private readonly ILogger<MadkhalMonitor> _logger;
    private readonly int _intervalSeconds;
    private bool? _lastAvailable;

    public MadkhalMonitor(
        IHttpClientFactory httpClientFactory,
        IEventPublisher publisher,
        IConnectivityTracker connectivity,
        IAgentIdentity identity,
        IOptions<RoutingOptions> routing,
        IOptions<MonitoringOptions> options,
        ILogger<MadkhalMonitor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("madkhal");
        _publisher = publisher;
        _connectivity = connectivity;
        _identity = identity;
        _routing = routing.Value;
        _logger = logger;
        _intervalSeconds = Math.Max(5, options.Value.MadkhalIntervalSeconds);
    }

    public string Name => nameof(MadkhalMonitor);

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
                _logger.LogError(ex, "Madkhal monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var available = await IsAvailableAsync(cancellationToken);
        _connectivity.SetMadkhal(available);

        if (_lastAvailable == available)
        {
            return;
        }

        _lastAvailable = available;
        await _publisher.PublishAsync(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _identity.AgentId,
            MonitorPointId = "madkhal",
            TimestampUtc = DateTime.UtcNow,
            EventType = EventType.MadkhalAvailability,
            Severity = available ? Severity.Info : Severity.Warning,
            Status = available ? EventStatus.Up : EventStatus.Down,
            Message = available ? "Madkhal server is available" : "Madkhal server is unavailable",
            AgentVersion = _identity.Version,
            Metadata = new Dictionary<string, string>
            {
                ["url"] = _routing.MadkhalServerUrl
            }
        }, cancellationToken);
    }

    private async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _routing.MadkhalTimeoutSeconds)));
            using var response = await _httpClient.GetAsync(_routing.MadkhalServerUrl, cts.Token);
            return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }
}
