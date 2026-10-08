using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

public sealed class TestEnvironmentTests
{
    [Fact]
    public void StateAndReportsUseTheIsolatedTestDirectory()
    {
        Assert.Equal(TestEnvironment.Home, AgentPaths.StateFolder);
        Assert.Equal(Path.Combine(TestEnvironment.Home, "Data"), AgentPaths.DataFolder);
        Assert.Equal(Path.Combine(TestEnvironment.Home, "Reports"), AgentPaths.ReportsFolder);
        Assert.Equal(Path.Combine(TestEnvironment.Home, "secret.key"), SecretProtector.KeyFile);
    }

    [UnixFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void TestDirectoryIsPrivateOnUnix()
    {
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(TestEnvironment.Home));
    }
}
