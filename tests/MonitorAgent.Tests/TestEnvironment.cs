using System.Runtime.CompilerServices;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

internal static class TestEnvironment
{
    internal static string Home { get; private set; } = string.Empty;

    // Set paths before any AgentPaths static initialization. Tests must not touch a real agent's state.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Home = Path.Combine(Path.GetTempPath(), "MonitorAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("MONITORAGENT_HOME", Home);
        SecretProtector.KeyFile = Path.Combine(Home, "secret.key");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked native SQLite file can be removed by the CI runner's temporary-directory cleanup.
        }
    }
}
