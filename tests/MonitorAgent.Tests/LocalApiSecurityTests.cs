using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Connectivity;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.LocalApi;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Shared.Constants;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

[Collection("ServiceSettings")]
public sealed class LocalApiSecurityTests
{
    private sealed class Notifications : INotificationStore
    {
        public IReadOnlyList<AgentIssueDto> GetNotifications() => [];
        public void ShowTemporary(AgentIssueDto message, TimeSpan duration) { }
    }
    private sealed class Issues : IIssueDataLogger
    {
        public void Record(string source, IReadOnlyList<AgentIssueDto> issues) { }
        public void RecordResolved(string source, IReadOnlyList<AgentIssueDto> issues) { }
    }
    private sealed class License : ILicenseState
    {
        public bool IsLicensed { get; set; }
        public LicenseStatusDto GetStatus() => new() { State = IsLicensed ? LicenseState.Active : LicenseState.NotActivated, IsValid = IsLicensed, Message = "fixture status" };
        public Task<LicenseActionResultDto> ActivateAsync(string productKey, CancellationToken ct) => Task.FromResult(new LicenseActionResultDto(true, "activated", GetStatus()));
        public Task<LicenseActionResultDto> DeactivateAsync(CancellationToken ct) => Task.FromResult(new LicenseActionResultDto(true, "deactivated", GetStatus()));
        public Task<LicenseStatusDto> RefreshAsync(CancellationToken ct) => Task.FromResult(GetStatus());
    }

    private sealed class App : IAsyncDisposable
    {
        public WebApplication Host { get; }
        public ServiceProvider Services { get; }
        public HttpClient Client => Host.GetTestClient();
        private App(WebApplication host, ServiceProvider services) { Host = host; Services = services; }
        public static async Task<App> Start(bool licensed, AgentAccessRole role = AgentAccessRole.Administrator, bool tcp = false, string key = "")
        {
            var cache = new FakeLocalConfigCache();
            cache.General.RemoteAccessKey = key;
            var services = new ServiceCollection().AddSingleton<ILocalConfigCache>(cache)
                .AddSingleton(new MonitorAgent.Service.Reports.ReportStore(NullLogger<MonitorAgent.Service.Reports.ReportStore>.Instance))
                .AddSingleton<IIssueDataLogger, Issues>().AddSingleton<INotificationStore, Notifications>()
                .AddSingleton<ILicenseState>(new License { IsLicensed = licensed }).AddSingleton<NotificationTrigger>()
                .AddSingleton<IMonitorHealthStore, MonitorHealthStore>()
                .AddSingleton<IConnectivityTracker, ConnectivityTracker>()
                .AddSingleton<IAgentIdentity>(new AgentIdentity(Options.Create(new AgentOptions()))).BuildServiceProvider();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            LocalApiHost.ConfigureServices(builder.Services);
            var host = builder.Build();
            new LocalApiHost(services, Options.Create(new LocalApiOptions()), NullLogger<LocalApiHost>.Instance).ConfigureApplication(host, role, tcp, "127.0.0.1");
            await host.StartAsync();
            return new App(host, services);
        }
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); await Services.DisposeAsync(); }
    }

    [Fact]
    public async Task Settings_get_and_invalid_put_preserve_storage_and_reject_client_protected_passwords()
    {
        var path = ServiceSettingsFile.PrimaryPath;
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            ServiceSettingsFile.WriteSection(System.Text.Json.Nodes.JsonNode.Parse("""{"General":{"MachineName":"fixture"},"MonitorPoints":[]}""")!.AsObject());
            var before = File.ReadAllBytes(path);
            await using var app = await App.Start(true);
            using var client = app.Client;
            var response = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(ApiRoutes.Settings);
            Assert.Equal("fixture", response!["General"]!["MachineName"]!.GetValue<string>());
            response["General"]!["ServicePort"] = 70000;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(ApiRoutes.Settings, response)).StatusCode);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(ApiRoutes.DatabaseTest, new DatabaseTestRequest(Login: new DatabaseLogin { Password = "aes:TEST-ONLY" }))).StatusCode);
            response["General"]!["ServicePort"] = 5050;
            SettingsContract.Merge(response, ServiceSettingsFile.ReadSection());
            using var savedResponse = await client.PutAsJsonAsync(ApiRoutes.Settings, response);
            Assert.True(savedResponse.StatusCode == HttpStatusCode.NoContent, await savedResponse.Content.ReadAsStringAsync());
            Assert.Equal("fixture", ServiceSettingsFile.ReadSection()["General"]!["MachineName"]!.GetValue<string>());
        }
        finally { if (original is null) File.Delete(path); else PrivateFile.WriteAllBytes(path, original); }
    }

    [Fact]
    public async Task Every_route_has_a_policy_and_viewers_cannot_run_administrator_actions()
    {
        await using var app = await App.Start(true, AgentAccessRole.Viewer);
        using var client = app.Client;
        client.DefaultRequestHeaders.Add("X-MonitorAgent-Role", "Administrator");
        var endpoints = ((IEndpointRouteBuilder)app.Host).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var policy = endpoint.Metadata.GetMetadata<RequiredAgentRole>();
            Assert.NotNull(policy);
            if (policy.Role != AgentAccessRole.Administrator) continue;
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), endpoint.RoutePattern.RawText))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApiRoutes.Status)).StatusCode);
    }

    [Fact]
    public async Task Tcp_checks_key_even_on_loopback_and_rejects_wrong_host_and_bodyless_posts()
    {
        await using var app = await App.Start(true, AgentAccessRole.Viewer, tcp: true, key: "TEST-ONLY-remote-key");
        using var client = app.Client;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Status)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(ApiRoutes.LicenseDeactivate, null)).StatusCode);
        client.DefaultRequestHeaders.Add(ApiRoutes.AccessKeyHeader, "TEST-ONLY-remote-key");
        client.DefaultRequestHeaders.Host = "127.0.0.1";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApiRoutes.Status)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(ApiRoutes.LicenseDeactivate, null)).StatusCode);
        client.DefaultRequestHeaders.Host = "attacker.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ApiRoutes.Status)).StatusCode);
    }

    [Fact]
    public async Task Unlicensed_routes_keep_the_license_required_contract()
    {
        await using var app = await App.Start(false);
        using var client = app.Client;
        var endpoints = ((IEndpointRouteBuilder)app.Host).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints)
        {
            var path = endpoint.RoutePattern.RawText!;
            if (path.StartsWith(ApiRoutes.License) || path is ApiRoutes.Status or ApiRoutes.Issues or ApiRoutes.Notifications) continue;
            path = path.Replace("{level:int}", "1").Replace("{type}", "cpu");
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(LicenseCodes.Required, body.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Status_and_license_actions_keep_their_response_shapes()
    {
        await using var app = await App.Start(false);
        using var client = app.Client;
        var status = await client.GetFromJsonAsync<JsonElement>(ApiRoutes.Status);
        foreach (var property in new[] { "agentId", "machineName", "status", "version", "uptime", "listenUrls", "addresses" })
            Assert.True(status.TryGetProperty(property, out _));
        foreach (var path in new[] { ApiRoutes.LicenseActivate, ApiRoutes.LicenseDeactivate })
        {
            using var response = await client.PostAsJsonAsync(path, new LicenseActivateRequest("TEST-ONLY-key"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("success").GetBoolean());
            Assert.True(body.TryGetProperty("status", out _));
        }
        Assert.Equal("fixture status", (await client.GetFromJsonAsync<JsonElement>(ApiRoutes.License)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Licensed_invalid_query_responses_are_preserved()
    {
        await using var app = await App.Start(true);
        using var client = app.Client;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ApiRoutes.HardwareLevels + "/6")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(ApiRoutes.ProcessesTop + "?sortBy=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ApiRoutes.Reports + "/unknown")).StatusCode);
    }
}
