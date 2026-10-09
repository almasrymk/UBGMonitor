using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MonitorAgent.Desktop.Views;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class SettingsSaveButtonTests
{
    [Fact]
    public void Save_button_uses_its_settings_context_instead_of_outer_view_context()
    {
        var client = new AgentApiClient();
        typeof(AgentApiClient).GetProperty(nameof(AgentApiClient.Role))!.SetValue(client, AgentAccessRole.Administrator);
        var settings = new SettingsViewModel(client) { IsLoaded = true, ServicePort = 0 };
        Assert.True(settings.SaveCommand.CanExecute(null));
        // The real view binds its inner DockPanel to Settings; its outer context is different.
        // Bypass XAML construction so this regression test requires no display or service.
        var view = (SettingsView)RuntimeHelpers.GetUninitializedObject(typeof(SettingsView));
        var button = new Button { DataContext = settings };
        typeof(SettingsView).GetMethod("SaveSettings_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [button, new RoutedEventArgs()]);
        Assert.Equal("The service port must be between 1 and 65535.", settings.StatusMessage);
    }
}
