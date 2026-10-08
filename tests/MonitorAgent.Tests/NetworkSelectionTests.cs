using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class NetworkSelectionTests
{
    [Fact]
    public void Cancel_after_temporary_null_restores_the_last_valid_address()
    {
        var settings = new SettingsViewModel(new AgentApiClient());
        settings.ServiceListenAddress = "192.168.1.8";
        settings.IsLoaded = true;
        var confirmations = 0;
        settings.ConfirmWarning = _ => { confirmations++; return Task.FromResult(false); };
        settings.ServiceListenAddress = null!;
        Assert.Equal(0, confirmations);
        settings.ServiceListenAddress = "0.0.0.0";
        Assert.Equal(1, confirmations);
        Assert.Equal("192.168.1.8", settings.ServiceListenAddress);
        Assert.True(settings.RemoteEnabled);
        Assert.True(settings.OpenFirewallPort);
    }
    [Fact]
    public void Temporary_null_selection_does_not_throw_or_change_flags_and_valid_selection_still_works()
    {
        var settings = new SettingsViewModel(new AgentApiClient());
        settings.ServiceListenAddress = "192.168.1.8";
        Assert.True(settings.RemoteEnabled);
        Assert.True(settings.OpenFirewallPort);
        Assert.False(settings.CanEditNetworkOptions);
        settings.ServiceListenAddress = null!; // Avalonia clears SelectedValue while changing selection.
        Assert.True(settings.CanEditNetworkOptions);
        Assert.True(settings.RemoteEnabled);
        Assert.True(settings.OpenFirewallPort);
        settings.ServiceListenAddress = "127.0.0.1";
        Assert.True(settings.CanEditNetworkOptions);
        Assert.False(settings.RemoteEnabled);
        Assert.False(settings.OpenFirewallPort);
    }
}
