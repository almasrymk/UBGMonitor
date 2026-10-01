using System.Reflection;
using System.Windows.Controls;

namespace MonitorAgent.UI.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }
}
