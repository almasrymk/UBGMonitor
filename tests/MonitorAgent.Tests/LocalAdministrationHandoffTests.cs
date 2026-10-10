using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using MonitorAgent.Shared.Constants;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Tests;

public sealed class LocalAdministrationHandoffTests
{
    private sealed class Transport(string id) : HttpMessageHandler
    {
        public List<string> Writes { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            if (request.Method != HttpMethod.Get) Writes.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.RequestUri!.AbsolutePath == ApiRoutes.Status)
                result = new { agentId = id, accessRole = "Administrator", status = "Running" };
            else if (request.RequestUri.AbsolutePath == ApiRoutes.RemoteAccess && request.Method == HttpMethod.Get)
                result = new RemoteAccessStatus(true, true, true, true, true, null);
            else result = new RemoteAccessResult(new(false, false, false, true, true, null));
            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Put ? HttpStatusCode.NoContent : HttpStatusCode.OK)
            { Content = JsonContent.Create(result, result.GetType()) });
        }
    }
    private static AgentApiClient Client(HttpClient http)
    {
        var client = new AgentApiClient(http);
        typeof(AgentApiClient).GetProperty(nameof(AgentApiClient.Role))!.SetValue(client, AgentAccessRole.Administrator);
        typeof(AgentApiClient).GetProperty(nameof(AgentApiClient.IsLocalTransport))!.SetValue(client, false);
        return client;
    }
    private static Task<bool> Adopt(AgentApiClient client, HttpClient local, AgentAccessRole role)
        => (Task<bool>)typeof(AgentApiClient).GetMethod("TryAdoptLocalAdministrationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [local, (Func<AgentAccessRole>)(() => role), CancellationToken.None])!;

    [Fact]
    public async Task Same_service_admin_moves_to_IPC_before_saving_loopback_and_disabling_remote_access()
    {
        using var remoteTransport = new Transport("TEST-ONLY-same-device");
        using var localTransport = new Transport("TEST-ONLY-same-device");
        using var remote = new HttpClient(remoteTransport) { BaseAddress = new Uri("https://192.0.2.10:5050") };
        using var local = new HttpClient(localTransport) { BaseAddress = new Uri("http://localhost") };
        var client = Client(remote);
        Assert.True(await Adopt(client, local, AgentAccessRole.Administrator));
        Assert.True(client.IsLocalTransport);
        Assert.True(client.CanAdminister);
        await new AppSettingsStore(client).SaveAsync(new UiAppSettings { General = new() { ServiceListenAddress = "127.0.0.1" } }, [], new("configure", false, false, false));
        Assert.Empty(remoteTransport.Writes);
        Assert.Equal(new[] { "PUT " + ApiRoutes.Settings, "POST " + ApiRoutes.RemoteAccess }, localTransport.Writes);
    }

    [Theory]
    [InlineData(AgentAccessRole.Viewer, "TEST-ONLY-same-device")]
    [InlineData(AgentAccessRole.None, "TEST-ONLY-same-device")]
    [InlineData(AgentAccessRole.Administrator, "TEST-ONLY-other-device")]
    public async Task Untrusted_local_role_or_different_service_preserves_working_remote_connection(AgentAccessRole localRole, string id)
    {
        using var remoteTransport = new Transport("TEST-ONLY-same-device");
        using var localTransport = new Transport(id);
        using var remote = new HttpClient(remoteTransport) { BaseAddress = new Uri("https://192.0.2.10:5050") };
        using var local = new HttpClient(localTransport) { BaseAddress = new Uri("http://localhost") };
        var client = Client(remote);
        Assert.False(await Adopt(client, local, localRole));
        Assert.False(client.IsLocalTransport);
        Assert.Null(await client.CheckConnectionAsync());
        Assert.Empty(remoteTransport.Writes);
        Assert.Empty(localTransport.Writes);
    }

    [Fact]
    public async Task Another_computers_address_does_not_switch_to_this_computers_IPC()
    {
        using var transport = new Transport("TEST-ONLY-remote-device");
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://192.0.2.10:5050") };
        var client = Client(http);
        typeof(AgentApiClient).GetField("_ownsHttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, true);
        Assert.False(await client.TryUseLocalAdministrationAsync());
        Assert.False(client.IsLocalTransport);
        Assert.True(client.CanAdminister);
        Assert.Empty(transport.Writes);
    }
}
