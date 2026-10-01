using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MonitorAgent.Desktop.Services;

namespace MonitorAgent.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        TextSnapping.Install();
        if (OperatingSystem.IsWindows())
        {
            // The Fluent theme gives every window Inter through this resource, whatever the default family is;
            // the Windows app uses Segoe UI.
            Resources["ContentControlThemeFontFamily"] = new FontFamily("Segoe UI");
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
