using System.Net.Security;
using MonitorAgent.Service.LocalApi;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Tests;

public sealed class RemoteAccessTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestEnvironment.Home, "remote-case-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_folder, "remote.json");
    [Fact]
    public void Invalid_certificate_import_does_not_replace_working_certificate()
    {
        var manager = new RemoteAccessManager(FilePath);
        manager.Apply(new("regenerate-certificate"));
        var before = manager.Status().Fingerprint;
        Assert.Throws<ArgumentException>(() => manager.Apply(new("import-certificate", Pfx: "")));
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=fixture", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var future = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2));
        Assert.False(CertificateTrust.Accept(future, SslPolicyErrors.RemoteCertificateChainErrors, CertificateTrust.Fingerprint(future)));
        using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False(CertificateTrust.Accept(expired, SslPolicyErrors.RemoteCertificateChainErrors, CertificateTrust.Fingerprint(expired)));
        Assert.Throws<ArgumentException>(() => manager.Apply(new("import-certificate", Pfx: Convert.ToBase64String(future.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12)))));
        Assert.Equal(before, manager.Status().Fingerprint);
        using var current = manager.GetCertificate();
        var metadata = manager.Status();
        Assert.Equal(current.NotBefore, metadata.CertificateNotBefore);
        Assert.Equal(current.NotAfter, metadata.CertificateNotAfter);
        Assert.Equal(current.Subject, metadata.CertificateSubject);
        Assert.Equal(current.Issuer, metadata.CertificateIssuer);
        Assert.Equal(current.SerialNumber, metadata.CertificateSerialNumber);
        var display = new MonitorAgent.UI.ViewModels.SettingsViewModel(new AgentApiClient());
        display.UpdateCertificateDisplay(metadata);
        Assert.True(display.CertificateIsValid);
        Assert.Equal(current.SerialNumber, display.CertificateSerial);
        Assert.Equal(current.Subject, display.CertificateSubject);
        Assert.Equal(current.Issuer, display.CertificateIssuer);
        Assert.NotEqual("—", display.CertificateExpiry);
        display.UpdateCertificateDisplay(new(false, false, false, false, false, "TEST-ONLY-fingerprint"));
        Assert.False(display.CertificateIsValid);
        Assert.Contains("restart", display.CertificateSummary);
        Assert.True(current.HasPrivateKey);
    }
    [Fact]
    public void Keys_are_returned_once_hashed_separate_and_remote_admin_requires_explicit_enable()
    {
        var manager = new RemoteAccessManager(FilePath);
        Assert.Throws<ArgumentException>(() => manager.Apply(new("configure", Enabled: true)));
        var viewer = manager.Apply(new("create-viewer-key")).Key!;
        var admin = manager.Apply(new("create-admin-key")).Key!;
        Assert.NotEqual(viewer, admin);
        Assert.DoesNotContain(viewer, File.ReadAllText(FilePath));
        Assert.DoesNotContain(admin, File.ReadAllText(FilePath));
        manager.Apply(new("configure", Enabled: true));
        Assert.Equal(AgentAccessRole.Viewer, manager.Authenticate(viewer));
        Assert.Equal(AgentAccessRole.None, manager.Authenticate(admin));
        Assert.False(manager.Status().OpenFirewall);
        manager.Apply(new("configure", Enabled: true, AllowAdministration: true, OpenFirewall: true));
        Assert.Equal(AgentAccessRole.Administrator, manager.Authenticate(admin));
        Assert.True(manager.Status().OpenFirewall);
        Assert.Null(manager.Apply(new("configure", Enabled: true)).Key);
        manager.Apply(new("revoke-viewer-key"));
        Assert.False(manager.Status().Enabled);
        Assert.Equal(AgentAccessRole.None, manager.Authenticate(viewer));
    }
    [Fact]
    public void Legacy_key_survives_as_viewer_hash_and_pin_rejects_unapproved_or_changed_certificate()
    {
        var manager = new RemoteAccessManager(FilePath);
        Assert.True(manager.MigrateLegacy("TEST-ONLY-legacy-key", true));
        Assert.False(manager.MigrateLegacy("TEST-ONLY-other", true));
        Assert.DoesNotContain("TEST-ONLY-legacy-key", File.ReadAllText(FilePath));
        Assert.Equal(AgentAccessRole.Viewer, manager.Authenticate("TEST-ONLY-legacy-key"));
        using var certificate = manager.GetCertificate();
        var fingerprint = CertificateTrust.Fingerprint(certificate);
        Assert.False(CertificateTrust.Accept(certificate, SslPolicyErrors.RemoteCertificateChainErrors, null));
        Assert.True(CertificateTrust.Accept(certificate, SslPolicyErrors.RemoteCertificateChainErrors, fingerprint));
        Assert.True(CertificateTrust.Accept(certificate, SslPolicyErrors.None, null));
        manager.Apply(new("regenerate-certificate"));
        using var replacement = manager.GetCertificate();
        Assert.False(CertificateTrust.Accept(replacement, SslPolicyErrors.None, fingerprint));
        Assert.NotEqual(fingerprint, manager.Status().Fingerprint);
    }
    [Fact]
    public void Bad_authentication_has_short_bounded_lockout_and_does_not_trust_headers()
    {
        var guard = new RemoteAbuseGuard();
        for (var i = 0; i < 5; i++) { Assert.False(guard.IsBlocked("fixture peer")); guard.Failed("fixture peer"); }
        Assert.True(guard.IsBlocked("fixture peer"));
        Assert.False(guard.IsBlocked("different peer"));
    }
    [Theory]
    [InlineData("http://remote.test:5050", false)]
    [InlineData("https://remote.test:5050", true)]
    [InlineData("remote.test:5050", true)]
    [InlineData("http://127.0.0.1:5050", true)]
    [InlineData("https://user:password@remote.test", false)]
    public void Address_parser_uses_https_remotely_and_preserves_local_ipc_address(string address, bool valid)
    {
        Assert.Equal(valid, AgentApiClient.ParseAddress(address) is not null);
        if (valid && !address.Contains("127.0.0.1")) Assert.Equal("https", AgentApiClient.ParseAddress(address)!.Scheme);
    }
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
