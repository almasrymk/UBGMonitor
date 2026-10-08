using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class AutomaticRemoteKeyTests
{
    private sealed class Transport(bool existing) : HttpMessageHandler
    {
        public string? Viewer = existing ? "TEST-ONLY-existing-viewer" : null;
        public string? Admin = existing ? "TEST-ONLY-existing-admin" : null;
        public List<string> Actions = [];
        private RemoteAccessStatus Status() => new(false, false, false, Viewer is not null, Admin is not null, null, Hash(Viewer), Hash(Admin));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object result;
            if (request.Method == HttpMethod.Get) result = Status();
            else
            {
                var action = (await request.Content!.ReadFromJsonAsync<RemoteAccessAction>(ct))!.Action;
                Actions.Add(action);
                var key = "TEST-ONLY-new-" + action;
                if (action == "create-viewer-key") Viewer = key;
                else if (action == "create-admin-key") Admin = key;
                else throw new InvalidOperationException("Unexpected action");
                result = new RemoteAccessResult(Status(), key);
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(result, result.GetType()) };
        }
        private static string? Hash(string? key) => key is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task Network_selection_creates_missing_keys_or_displays_existing_keys_without_enabling_admin(bool existing, bool accept, bool cached)
    {
        using var transport = new Transport(existing);
        using var http = new HttpClient(transport);
        var client = new AgentApiClient(http);
        typeof(AgentApiClient).GetProperty(nameof(AgentApiClient.Role))!.SetValue(client, AgentAccessRole.Administrator);
        var store = new RemoteKeyDisplayStore(Path.Combine(TestEnvironment.Home, Guid.NewGuid() + ".json"));
        if (cached) { store.Save("viewer", transport.Viewer!); store.Save("admin", transport.Admin!); }
        var settings = new SettingsViewModel(client, store);
        settings.ServiceListenAddress = "192.168.1.8";
        settings.ConfirmWarning = _ => Task.FromResult(accept);
        var confirm = typeof(SettingsViewModel).GetMethod("ConfirmNetworkSelectionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)confirm.Invoke(settings, ["127.0.0.1", "192.168.1.8"])!;
        Assert.False(settings.RemoteAdministration);
        Assert.False(settings.RemoteKeyPanelOpen);
        if (accept)
        {
            Assert.Equal(transport.Viewer, settings.ViewerKeyDisplay);
            Assert.Equal(transport.Admin, settings.AdminKeyDisplay);
            Assert.Equal(cached ? 0 : 2, transport.Actions.Count);
            Assert.True(settings.RemoteEnabled);
            var prepare = typeof(SettingsViewModel).GetMethod("PrepareRemoteKeysAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await (Task)prepare.Invoke(settings, null)!;
            Assert.Equal(cached ? 0 : 2, transport.Actions.Count); // Later visits retain both keys.
        }
        else { Assert.Empty(transport.Actions); Assert.Equal("127.0.0.1", settings.ServiceListenAddress); }
    }
}
