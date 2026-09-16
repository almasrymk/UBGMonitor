using ClientAgent.Service.Config;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Options;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class MadkhalMonitor : BackgroundService, IMonitoringModule
{
    private readonly HttpClient _httpClient;
    private readonly IConnectivityTracker _connectivity;
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly RoutingOptions _routing;
    private readonly ILogger<MadkhalMonitor> _logger;
    private readonly int _intervalSeconds;
    private bool? _lastAvailable;

    public MadkhalMonitor(
        IHttpClientFactory httpClientFactory,
        IConnectivityTracker connectivity,
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        IOptions<RoutingOptions> routing,
        IOptions<MonitoringOptions> options,
        ILogger<MadkhalMonitor> logger)
    {
        _httpClient = httpClientFactory.CreateClient("madkhal");
        _connectivity = connectivity;
        _configCache = configCache;
        _health = health;
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
        _health.SetPointHealth("madkhal", available);

        var config = await _configCache.GetConfigAsync(cancellationToken);
        foreach (var point in config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Madkhal))
        {
            _health.SetPointHealth(point.MonitorPointId, available);
        }

        if (available)
        {
            _health.ClearIssue("madkhal");
        }
        else
        {
            _health.SetIssue("madkhal", "Warning", "Madkhal unavailable", IssueText.MadkhalDown(), "madkhal");
        }

        if (_lastAvailable == available)
        {
            return;
        }

        _lastAvailable = available;
        if (available)
        {
            _logger.LogInformation("Madkhal server is available at {Url}", _routing.MadkhalServerUrl);
        }
        else
        {
            _logger.LogWarning("Madkhal server is unavailable at {Url}", _routing.MadkhalServerUrl);
        }
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
