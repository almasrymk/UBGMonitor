using System.Windows;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ClientPreferences.MoveLegacyFolder();
        base.OnStartup(e);
    }
}
