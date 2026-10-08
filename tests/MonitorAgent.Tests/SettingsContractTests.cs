using System.Text.Json.Nodes;
using MonitorAgent.Service.Config;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Tests;

public sealed class SettingsContractTests
{
    private static JsonObject Stored() => JsonNode.Parse("""
        {"General":{"RemoteAccessKey":"TEST-ONLY-key"},"MonitorPoints":[{"MonitorPointId":"db","Type":"Database","Database":{"Server":"original.test","Username":"monitor","Password":"dpapi:TEST-ONLY"},"Metadata":{"Token":"TEST-ONLY-token"}}],"Serilog":{"WriteTo":"attacker"}}
        """)!.AsObject();

    [Fact]
    public void Ordinary_edit_preserves_non_database_metadata_and_rejects_malformed_conditions()
    {
        var stored = Stored();
        stored["MonitorPoints"]!.AsArray().Add(JsonNode.Parse("""{"MonitorPointId":"device","Type":"Device","Metadata":{"Tag":"private-fixture"}}"""));
        var edit = SettingsContract.PublicSettings(stored);
        Assert.Equal("private-fixture", SettingsContract.Merge(edit, stored)["MonitorPoints"]![1]!["Metadata"]!["Tag"]!.GetValue<string>());
        edit["Conditions"] = new JsonArray("invalid");
        Assert.Throws<ArgumentException>(() => SettingsContract.Merge(edit, stored));
        Assert.Equal("private-fixture", stored["MonitorPoints"]![1]!["Metadata"]!["Tag"]!.GetValue<string>());
    }

    [Fact]
    public void Public_settings_exclude_secrets_blobs_and_unknown_sections_and_keep_editable_fields()
    {
        var result = SettingsContract.PublicSettings(Stored());
        var text = result.ToJsonString();
        Assert.DoesNotContain("TEST-ONLY", text);
        Assert.DoesNotContain("dpapi:", text);
        Assert.False(result.ContainsKey("Serilog"));
        Assert.True(result["General"]!["HasRemoteAccessKey"]!.GetValue<bool>());
        Assert.True(result["MonitorPoints"]![0]!["Database"]!["HasPassword"]!.GetValue<bool>());
        Assert.Equal("original.test", result["MonitorPoints"]![0]!["Database"]!["Server"]!.GetValue<string>());
    }

    [Fact]
    public void Ordinary_edit_preserves_stored_password_key_and_metadata()
    {
        var stored = Stored();
        var edit = SettingsContract.PublicSettings(stored);
        edit["General"]!["MachineName"] = "fixture machine";
        edit["General"]!["RemoteAccessKey"] = "TEST-ONLY-replacement";
        var result = SettingsContract.Merge(edit, stored);
        Assert.Equal("dpapi:TEST-ONLY", result["MonitorPoints"]![0]!["Database"]!["Password"]!.GetValue<string>());
        Assert.Equal("TEST-ONLY-key", result["General"]!["RemoteAccessKey"]!.GetValue<string>());
        Assert.Equal("TEST-ONLY-token", result["MonitorPoints"]![0]!["Metadata"]!["Token"]!.GetValue<string>());
        Assert.Equal("fixture machine", result["General"]!["MachineName"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Server", "redirect.test")]
    [InlineData("Username", "other")]
    [InlineData("Database", "other")]
    public void Password_is_not_reused_for_a_changed_target(string field, string value)
    {
        var stored = Stored();
        var edit = SettingsContract.PublicSettings(stored);
        edit["MonitorPoints"]![0]!["Database"]![field] = value;
        Assert.Throws<ArgumentException>(() => SettingsContract.Merge(edit, stored));
        Assert.Equal("original.test", stored["MonitorPoints"]![0]!["Database"]!["Server"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("dpapi:TEST-ONLY")]
    [InlineData("dpapi2:TEST-ONLY")]
    [InlineData("aes:TEST-ONLY")]
    public void Protected_client_password_is_refused_by_both_boundaries(string password)
    {
        var stored = Stored();
        var edit = SettingsContract.PublicSettings(stored);
        edit["MonitorPoints"]![0]!["Database"]!["Password"] = password;
        Assert.Throws<ArgumentException>(() => SettingsContract.Merge(edit, stored));
        Assert.Throws<ArgumentException>(() => DatabaseTestResolver.Resolve(new(Login: new DatabaseLogin { Password = password }), stored));
    }

    [Fact]
    public void New_plain_password_and_stored_test_resolve_without_client_decryption()
    {
        var stored = Stored();
        var edit = SettingsContract.PublicSettings(stored);
        edit["MonitorPoints"]![0]!["Database"]!["Server"] = "new.test";
        edit["MonitorPoints"]![0]!["Database"]!["Password"] = "TEST-ONLY-new-password";
        Assert.Equal("TEST-ONLY-new-password", SettingsContract.Merge(edit, stored)["MonitorPoints"]![0]!["Database"]!["Password"]!.GetValue<string>());
        Assert.Equal("dpapi:TEST-ONLY", DatabaseTestResolver.Resolve(new("db"), stored).Password);
        Assert.Throws<ArgumentException>(() => DatabaseTestResolver.Resolve(new("db", new DatabaseLogin { Server = "redirect.test" }), stored));
    }

    [Theory]
    [InlineData("ServicePort", -1)]
    [InlineData("ServicePort", 65536)]
    [InlineData("RefreshInterval", 0)]
    [InlineData("DataRetentionDays", 366)]
    public void Invalid_settings_are_rejected_without_mutating_stored_data(string field, int value)
    {
        var stored = Stored();
        var original = stored.ToJsonString();
        var edit = SettingsContract.PublicSettings(stored);
        edit["General"]![field] = value;
        Assert.Throws<ArgumentException>(() => SettingsContract.Merge(edit, stored));
        Assert.Equal(original, stored.ToJsonString());
    }

    [Theory]
    [InlineData("file:///tmp/fixture")]
    [InlineData("javascript:alert(1)")]
    [InlineData("custom://fixture")]
    public void Unsafe_schemes_and_remote_applications_are_not_launched(string address)
    {
        Assert.NotNull(MonitorPointLauncher.Open(new MonitorPoint { Type = MonitorPointType.Website, Address = address }));
        Assert.NotNull(MonitorPointLauncher.Open(new MonitorPoint { Type = MonitorPointType.Application, Address = address }, false, true));
        Assert.False(MonitorPointLauncher.IsAllowedApplicationPath(@"\\fixture\share\app.exe"));
    }
}
