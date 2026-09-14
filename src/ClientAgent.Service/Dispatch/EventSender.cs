using System.Net.Http.Json;
using System.Text.Json;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Options;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace ClientAgent.Service.Dispatch;

public interface IEventSender
{
    Task<bool> SendAsync(IReadOnlyList<MonitoringEvent> events, Destination destination, CancellationToken cancellationToken);
}

public sealed class EventSender : IEventSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly RoutingOptions _options;
    private readonly IConnectivityTracker _connectivity;
    private readonly ILogger<EventSender> _logger;
    private readonly ResiliencePipeline _pipeline;

    public EventSender(
        HttpClient httpClient,
        IOptions<RoutingOptions> options,
        IConnectivityTracker connectivity,
        ILogger<EventSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _connectivity = connectivity;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TaskCanceledException>()
            })
            .Build();
    }

    public async Task<bool> SendAsync(IReadOnlyList<MonitoringEvent> events, Destination destination, CancellationToken cancellationToken)
    {
        if (destination == Destination.Local || events.Count == 0)
        {
            return destination == Destination.Local;
        }

        var url = destination == Destination.Madkhal
            ? Combine(_options.MadkhalServerUrl, "/api/events")
            : Combine(_options.CentralApiUrl, "/api/events");

        var timeout = destination == Destination.Madkhal
            ? _options.MadkhalTimeoutSeconds
            : _options.CentralTimeoutSeconds;

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeout)));
                using var response = await _httpClient.PostAsJsonAsync(url, events, JsonOptions, cts.Token);
                response.EnsureSuccessStatusCode();
            }, cancellationToken);

            if (destination == Destination.Central)
            {
                _connectivity.SetCentral(true);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Count} events to {Destination}", events.Count, destination);
            if (destination == Destination.Central)
            {
                _connectivity.SetCentral(false);
            }

            return false;
        }
    }

    private static string Combine(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}{path}";
}
