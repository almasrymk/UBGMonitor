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
        _logger.LogInformation("[Madkhal] Checking the Madkhal server {Url}...", _routing.MadkhalServerUrl);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var available = await IsAvailableAsync(cancellationToken);
        if (available)
        {
            _logger.LogInformation("[Madkhal] OK - server is available ({Ms} ms)", watch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning("[Madkhal] WARNING - server is unavailable ({Ms} ms)", watch.ElapsedMilliseconds);
        }

        _connectivity.SetMadkhal(available);
        double? responseMs = available ? watch.ElapsedMilliseconds : null;
        _health.SetPointHealth("madkhal", available, responseMs: responseMs);

        var config = await _configCache.GetConfigAsync(cancellationToken);
        foreach (var point in config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Madkhal))
        {
            _health.SetPointHealth(point.MonitorPointId, available, responseMs: responseMs);
        }

        if (available)
        {
            _health.ClearIssue("madkhal");
        }
        else
        {
            _health.SetIssue("madkhal", "Warning", "Madkhal unavailable", IssueText.MadkhalDown(), "madkhal");
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
