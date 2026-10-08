using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Connectivity;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Platform;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Tests;

public sealed class RoutingSecurityTests
{
    private sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls;
        public Uri? LastUri;
        public bool WaitForCancellation;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AgentRuntimeConfig
            {
                ConfigVersion = "9", AgentId = "configured", DatabaseConnectionString = "Data Source=/untrusted/network.db",
                CpuCriticalThreshold = 75, MonitorPoints = [new MonitorPoint { MonitorPointId = "configured-point" }]
            }) };
        }
    }
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public int Count;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> format) => Count++;
    }
    private static IAgentIdentity Identity() => new AgentIdentity(Options.Create(new AgentOptions()));

    [Fact]
    public async Task Central_request_timeout_cancels_transport_and_preserves_cached_configuration()
    {
        using var transport = new Transport { WaitForCancellation = true };
        var cache = new FakeLocalConfigCache();
        var before = (await cache.GetConfigAsync()).ConfigVersion;
        using var puller = new ConfigPuller(transport, cache, Identity(), new ConnectivityTracker(),
            Options.Create(new RoutingOptions { CentralApiUrl = "https://central.test", CentralTimeoutSeconds = 1 }), new RecordingLogger<ConfigPuller>());
        await puller.PullAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, transport.Calls);
        Assert.Equal(before, (await cache.GetConfigAsync()).ConfigVersion);
    }

    [Fact]
    public async Task Empty_routing_is_silent_and_does_not_create_health_issues()
    {
        using var transport = new Transport();
        var cache = new FakeLocalConfigCache();
        var connectivity = new ConnectivityTracker();
        using var services = new ServiceCollection().AddSingleton<NotificationTrigger>().BuildServiceProvider();
        var health = new MonitorHealthStore(services);
        var centralLog = new RecordingLogger<ConfigPuller>();
        var madkhalLog = new RecordingLogger<MadkhalMonitor>();
        var routing = Options.Create(new RoutingOptions());
        using var puller = new ConfigPuller(transport, cache, Identity(), connectivity, routing, centralLog);
        using var monitor = new MadkhalMonitor(transport, connectivity, cache, health, routing, Options.Create(new MonitoringOptions()), madkhalLog);
        await puller.PullAsync(default);
        await monitor.RunCycleAsync(default);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(0, centralLog.Count + madkhalLog.Count);
        Assert.Empty(health.GetIssues());
        Assert.Null(health.GetLastCheckedUtc("madkhal"));
    }

    [Theory]
    [InlineData("http://central.test")]
    [InlineData("/relative")]
    [InlineData("ftp://central.test")]
    public async Task Unsafe_central_url_is_idle_and_warns_once(string url)
    {
        using var transport = new Transport();
        var log = new RecordingLogger<ConfigPuller>();
        using var puller = new ConfigPuller(transport, new FakeLocalConfigCache(), Identity(), new ConnectivityTracker(), Options.Create(new RoutingOptions { CentralApiUrl = url }), log);
        await puller.PullAsync(default);
        await puller.PullAsync(default);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(1, log.Count);
    }

    [Theory]
    [InlineData("https://central.test")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    public async Task Configured_central_preserves_fields_and_ignores_network_database_path(string url)
    {
        using var transport = new Transport();
        var cache = new FakeLocalConfigCache();
        var tracker = new ConnectivityTracker();
        using var puller = new ConfigPuller(transport, cache, Identity(), tracker, Options.Create(new RoutingOptions { CentralApiUrl = url }), new RecordingLogger<ConfigPuller>());
        await puller.PullAsync(default);
        var config = await cache.GetConfigAsync();
        Assert.Equal(1, transport.Calls);
        Assert.Equal("9", config.ConfigVersion);
        Assert.Equal("configured", config.AgentId);
        Assert.Equal(75, config.CpuCriticalThreshold);
        Assert.Equal("configured-point", Assert.Single(config.MonitorPoints).MonitorPointId);
        Assert.Equal($"Data Source={Path.Combine(AgentPaths.StateFolder, "local.db")}", config.DatabaseConnectionString);
        Assert.True(tracker.CentralAvailable);
    }

    [Fact]
    public async Task Configured_madkhal_keeps_its_success_path()
    {
        using var transport = new Transport();
        using var services = new ServiceCollection().AddSingleton<NotificationTrigger>().BuildServiceProvider();
        var health = new MonitorHealthStore(services);
        var tracker = new ConnectivityTracker();
        using var monitor = new MadkhalMonitor(transport, tracker, new FakeLocalConfigCache(), health, Options.Create(new RoutingOptions { MadkhalServerUrl = "http://madkhal.test" }), Options.Create(new MonitoringOptions()), new RecordingLogger<MadkhalMonitor>());
        await monitor.RunCycleAsync(default);
        Assert.Equal(1, transport.Calls);
        Assert.True(tracker.MadkhalAvailable);
        Assert.True(health.GetIsUp("madkhal"));
        Assert.Empty(health.GetIssues());
    }
}
