using System.Text.Json.Nodes;
using MonitorAgent.Service.Config;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

public sealed class StateMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(TestEnvironment.Home, "migration-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_root, "state");
    private string Install => Path.Combine(_root, "install");
    public StateMigrationTests() { Directory.CreateDirectory(Install); }
    [Fact]
    public void Upgrade_preserves_settings_password_license_history_and_is_idempotent()
    {
        var legacy = Path.Combine(State, "publish");
        Directory.CreateDirectory(Path.Combine(legacy, "Data"));
        Directory.CreateDirectory(Path.Combine(legacy, "Reports"));
        File.WriteAllText(Path.Combine(legacy, "appsettings.json"), """{"Setting":{"General":{"MachineName":"fixture"},"MonitorPoints":[{"MonitorPointId":"real","Database":{"Password":"dpapi:TEST-ONLY"}}]},"Serilog":{"MinimumLevel":"Fatal"}}""");
        File.WriteAllText(Path.Combine(legacy, "Data", "reports.db"), "fixture history");
        File.WriteAllText(Path.Combine(State, "license.json"), "fixture license");
        StateMigration.Run(State, Install, true);
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(State, "settings.json")))!;
        Assert.Null(settings["Serilog"]);
        Assert.Equal("dpapi:TEST-ONLY", settings["MonitorPoints"]![0]!["Database"]!["Password"]!.GetValue<string>());
        Assert.Equal("fixture history", File.ReadAllText(Path.Combine(State, "Data", "reports.db")));
        Assert.Equal("fixture license", File.ReadAllText(Path.Combine(State, "license.json")));
        var backupCount = Directory.GetFiles(Path.Combine(State, "backups")).Length;
        StateMigration.Run(State, Install, true);
        Assert.Equal(backupCount, Directory.GetFiles(Path.Combine(State, "backups")).Length);
    }
    [Fact]
    public void Existing_settings_survive_and_interrupted_copy_is_completed()
    {
        Directory.CreateDirectory(Path.Combine(State, "publish", "Data"));
        Directory.CreateDirectory(Path.Combine(State, "Data"));
        PrivateFile.WriteAllText(Path.Combine(State, "settings.json"), "{\"General\":{\"MachineName\":\"newer\"}}");
        File.WriteAllText(Path.Combine(State, "publish", "appsettings.json"), "{\"Setting\":{\"General\":{\"MachineName\":\"older\"}}}");
        File.WriteAllText(Path.Combine(State, "publish", "Data", "sample"), "same");
        File.WriteAllText(Path.Combine(State, "Data", "sample"), "same");
        StateMigration.Run(State, Install, true);
        Assert.Contains("newer", File.ReadAllText(Path.Combine(State, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(State, "publish", "Data")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(State, "backups")));
    }
    [Fact]
    public void Fresh_install_has_no_personal_settings_and_conflicting_data_is_not_overwritten()
    {
        File.WriteAllText(Path.Combine(Install, "appsettings.json"), "{}");
        StateMigration.Run(State, Install, false);
        Assert.False(File.Exists(Path.Combine(State, "settings.json")));
        Directory.CreateDirectory(Path.Combine(State, "publish", "Data"));
        Directory.CreateDirectory(Path.Combine(State, "Data"));
        File.WriteAllText(Path.Combine(State, "publish", "Data", "sample"), "older");
        File.WriteAllText(Path.Combine(State, "Data", "sample"), "newer");
        Assert.Throws<IOException>(() => StateMigration.Run(State, Install, true));
        Assert.Equal("newer", File.ReadAllText(Path.Combine(State, "Data", "sample")));
        Assert.Equal("older", File.ReadAllText(Path.Combine(State, "publish", "Data", "sample")));
    }
    [UnixFact]
    public void Migration_and_atomic_replacement_create_owner_only_files()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(State);
        File.WriteAllText(Path.Combine(State, "appsettings.previous.json"), "{\"Setting\":{\"MonitorPoints\":[]}}");
        StateMigration.Run(State, Install, false);
        foreach (var file in Directory.GetFiles(State, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "appsettings.previous.json") continue;
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        PrivateFile.WriteAllText(Path.Combine(State, "settings.json"), "{}");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(State, "settings.json")));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
