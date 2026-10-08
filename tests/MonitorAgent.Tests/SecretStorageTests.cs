using System.Security.Cryptography;
using System.Text;
using MonitorAgent.Shared.Security;
using MonitorAgent.Shared.Models;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Tests;

public sealed class SecretStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sensitive_clipboard_clears_only_its_unchanged_value(bool changed)
    {
        string? clipboard = null;
        var cleared = false;
        await SensitiveClipboard.CopyAsync("TEST-ONLY-copy", value => { clipboard = value; return Task.CompletedTask; },
            () => Task.FromResult(clipboard), () => { cleared = true; clipboard = null; return Task.CompletedTask; },
            () => { if (changed) clipboard = "user's other content"; return Task.CompletedTask; });
        Assert.Equal(!changed, cleared);
        Assert.Equal(changed ? "user's other content" : null, clipboard);
    }
    [WindowsFact]
    public void New_secrets_use_current_user_and_legacy_machine_values_can_be_migrated()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string plain = "TEST-ONLY-fixture-password";
        var entropy = Encoding.UTF8.GetBytes("ClientAgent.Database.v1");
        var legacy = "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), entropy, DataProtectionScope.LocalMachine));
        Assert.Equal(plain, SecretProtector.Unprotect(legacy));
        var migrated = SecretProtector.ReprotectLegacy(legacy);
        Assert.StartsWith("dpapi2:", migrated);
        Assert.Equal(plain, SecretProtector.Unprotect(migrated));
        Assert.Equal(migrated, SecretProtector.ReprotectLegacy(migrated));
        Assert.Equal(plain, Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(migrated[7..]), entropy, DataProtectionScope.CurrentUser)));
    }
    [UnixFact]
    public void Unsafe_key_modes_and_symbolic_links_are_refused_and_valid_key_is_accepted()
    {
        if (OperatingSystem.IsWindows()) return;
        var parent = Path.Combine(TestEnvironment.Home, "key-check-" + Guid.NewGuid().ToString("N"));
        PrivateFile.EnsureDirectory(parent);
        var path = Path.Combine(parent, "secret.key");
        try
        {
            PrivateFile.WriteAllBytes(path, RandomNumberGenerator.GetBytes(32));
            SecretProtector.VerifyUnixKey(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            Assert.Throws<UnauthorizedAccessException>(() => SecretProtector.VerifyUnixKey(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var link = Path.Combine(parent, "link.key");
            File.CreateSymbolicLink(link, path);
            Assert.Throws<UnauthorizedAccessException>(() => SecretProtector.VerifyUnixKey(link));
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
            Assert.Throws<UnauthorizedAccessException>(() => SecretProtector.VerifyUnixKey(path));
        }
        finally { Directory.Delete(parent, true); }
    }
    [Fact]
    public void Driver_failure_does_not_echo_credentials_and_keeps_certificate_hint()
    {
        var login = new DatabaseLogin { TlsMode = DatabaseTlsMode.Verify, Password = "TEST-ONLY-password" };
        var result = DatabaseMonitor.DescribeFailure(login, new Exception("TLS certificate failed; Password=TEST-ONLY-password; Token=TEST-ONLY-token"));
        Assert.DoesNotContain("TEST-ONLY", result);
        Assert.Contains("Server identity verification failed", result);
        var text = LogRedaction.Text("Password=TEST-ONLY-password; Token=TEST-ONLY-token https://user:TEST-ONLY-password@fixture.test/");
        Assert.DoesNotContain("TEST-ONLY", text);
    }
}
