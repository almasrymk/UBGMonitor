using System.Net.Http.Json;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Config;

public interface IConfigPuller
{
    Task PullAsync(CancellationToken cancellationToken);
}

public sealed class ConfigPuller : BackgroundService, IConfigPuller
{
    private readonly HttpClient _httpClient;
    private readonly ILocalConfigCache _cache;
    private readonly IAgentIdentity _identity;
    private readonly IConnectivityTracker _connectivity;
    private readonly RoutingOptions _routing;
    private readonly ILogger<ConfigPuller> _logger;

    public ConfigPuller(
        IHttpClientFactory httpClientFactory,
        ILocalConfigCache cache,
        IAgentIdentity identity,
        IConnectivityTracker connectivity,
        IOptions<RoutingOptions> routing,
        ILogger<ConfigPuller> logger)
    {
        _httpClient = httpClientFactory.CreateClient("central");
        _cache = cache;
        _identity = identity;
        _connectivity = connectivity;
        _routing = routing.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PullAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Config pull failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    public async Task PullAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_routing.CentralApiUrl.TrimEnd('/')}{ApiRoutes.AgentConfig}?agentId={Uri.EscapeDataString(_identity.AgentId)}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _routing.CentralTimeoutSeconds)));
            var config = await _httpClient.GetFromJsonAsync<AgentRuntimeConfig>(url, cts.Token);
            if (config is not null)
            {
                await _cache.SaveConfigAsync(config, cancellationToken);
                _connectivity.SetCentral(true);
                _logger.LogInformation("Applied config version {Version}", config.ConfigVersion);
            }
        }
        catch (Exception ex)
        {
            _connectivity.SetCentral(false);
            _logger.LogWarning(ex, "Central config is unavailable; using local cache");
        }
    }
}
