using System.Net;
using System.Net.Http.Json;
using MonitorAgent.Shared.Constants;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Models;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class CombinedSettingsSaveTests
{
    private sealed class Transport : HttpMessageHandler
    {
        public bool Viewer = true, Admin = true, FailSettings, FailRemote;
        public List<string> Writes = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new RemoteAccessStatus(false, false, false, Viewer, Admin, null)) });
            Writes.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            var failed = request.Method == HttpMethod.Put ? FailSettings : FailRemote;
            return Task.FromResult(new HttpResponseMessage(failed ? HttpStatusCode.ServiceUnavailable : request.Method == HttpMethod.Put ? HttpStatusCode.NoContent : HttpStatusCode.OK)
            { Content = JsonContent.Create(new RemoteAccessResult(new(true, true, true, Viewer, Admin, null))) });
        }
    }
    private static AgentApiClient Client(HttpClient http)
    {
        var client = new AgentApiClient(http);
        // Emulate the Administrator identity supplied by the local IPC transport, without opening a pipe.
        typeof(AgentApiClient).GetProperty(nameof(AgentApiClient.Role))!.SetValue(client, AgentAccessRole.Administrator);
        return client;
    }
    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public async Task Save_validates_keys_and_applies_remote_only_after_settings_succeed(bool viewer, bool admin, bool failSettings, bool failRemote)
    {
        using var transport = new Transport { Viewer = viewer, Admin = admin, FailSettings = failSettings, FailRemote = failRemote };
        using var http = new HttpClient(transport);
        var store = new AppSettingsStore(Client(http));
        var save = () => store.SaveAsync(new UiAppSettings(), [], new("configure", true, true, true));
        if (!viewer || !admin || failSettings || failRemote) await Assert.ThrowsAsync<IOException>(save);
        else await save();
        if (!viewer || !admin) Assert.Empty(transport.Writes);
        else if (failSettings) Assert.Equal(new[] { "PUT " + ApiRoutes.Settings }, transport.Writes);
        else Assert.Equal(new[] { "PUT " + ApiRoutes.Settings, "POST " + ApiRoutes.RemoteAccess }, transport.Writes);
    }
    [Fact]
    public void Remote_checkbox_changes_enable_dirty_tracking_and_reset_restores_saved_values()
    {
        var settings = new SettingsViewModel(new AgentApiClient());
        Assert.False(settings.HasChanges);
        settings.RemoteAdministration = true;
        Assert.True(settings.HasChanges);
        settings.ResetCommand.Execute(null);
        Assert.False(settings.RemoteAdministration);
        Assert.False(settings.HasChanges);
    }
}
