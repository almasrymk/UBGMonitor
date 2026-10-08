using System.Security.Cryptography;
using System.Text;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class RemoteKeyDisplayTests
{
    [Fact]
    public void Keys_persist_encrypted_and_only_match_the_current_service_hash()
    {
        var path = Path.Combine(TestEnvironment.Home, Guid.NewGuid() + ".json");
        var store = new RemoteKeyDisplayStore(path);
        const string viewer = "TEST-ONLY-viewer-display-fixture";
        const string admin = "TEST-ONLY-admin-display-fixture";
        store.Save("viewer", viewer);
        store.Save("admin", admin);
        var disk = File.ReadAllText(path);
        Assert.DoesNotContain(viewer, disk);
        Assert.DoesNotContain(admin, disk);
        var reloaded = new RemoteKeyDisplayStore(path);
        Assert.Equal(viewer, reloaded.Load("viewer", Hash(viewer)));
        Assert.Equal(admin, reloaded.Load("admin", Hash(admin)));
        Assert.Empty(reloaded.Load("viewer", Hash(admin)));
        Assert.Empty(reloaded.Load("admin", null));
        store.Save("viewer", "TEST-ONLY-replacement");
        Assert.Empty(reloaded.Load("viewer", Hash(viewer)));
        Assert.Equal(admin, reloaded.Load("admin", Hash(admin)));
    }
    [Fact]
    public void Access_toggle_does_not_implicitly_enable_admin()
    {
        var settings = new SettingsViewModel(new AgentApiClient());
        settings.RemoteEnabled = true;
        Assert.False(settings.RemoteAdministration);
        Assert.Equal("Key unavailable here; regenerate to display it.", settings.ViewerKeyDisplay);
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
