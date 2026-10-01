using Avalonia;
using Avalonia.Media;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClientPreferences.MoveLegacyFolder();
        // The app runs as the signed-in user, who cannot read the service's root-only key; the app's own
        // secrets (the access key in client.json) use a key in the user's folder instead.
        SecretProtector.KeyFile = Path.Combine(AppPaths.Folder, "secret.key");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .LogToTrace();
}
