#if !DEBUG
using System.Reflection;
using System.Text.Json.Nodes;
using MonitorAgent.Service.Config;

namespace MonitorAgent.Tests;

public sealed class ReleaseSettingsTests
{
    [Fact]
    public void Release_has_no_development_copy_writer_but_keeps_primary_settings_writes()
    {
        Assert.Null(typeof(ServiceSettingsFile).GetMethod("DevelopmentCopies", BindingFlags.Static | BindingFlags.NonPublic));
        var path = ServiceSettingsFile.PrimaryPath;
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            ServiceSettingsFile.WriteSection(new JsonObject { ["General"] = new JsonObject { ["MachineName"] = "TEST-ONLY-machine" }, ["MonitorPoints"] = new JsonArray() });
            var saved = ServiceSettingsFile.ReadSection();
            Assert.Equal("TEST-ONLY-machine", saved["General"]!["MachineName"]!.GetValue<string>());
            Assert.Empty(saved["MonitorPoints"]!.AsArray());
        }
        finally
        {
            if (original is null) File.Delete(path);
            else File.WriteAllBytes(path, original);
        }
    }
}
#endif
