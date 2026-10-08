using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.LocalApi;

public sealed class RemoteAccessManager
{
    private sealed record State(bool Enabled = false, bool AllowAdministration = false, bool OpenFirewall = false,
        string? ViewerHash = null, string? AdminHash = null, string? ProtectedPfx = null, bool LegacyMigrated = false);
    private readonly string _path;
    private readonly object _gate = new();
    public RemoteAccessManager() : this(Path.Combine(AgentPaths.StateFolder, "remote-access.json")) { }
    public RemoteAccessManager(string path) => _path = path;
    private State Read() => File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? throw new IOException("Remote state is invalid.") : new();
    private void Save(State state) => PrivateFile.WriteAllText(_path, JsonSerializer.Serialize(state));
    public string Revision { get { lock (_gate) { return File.Exists(_path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_path))) : ""; } } }
    public bool MigrateLegacy(string key, bool enabled)
    {
        lock (_gate)
        {
            var state = Read();
            if (state.LegacyMigrated) return false;
            if (key.Length == 0) { Save(state with { LegacyMigrated = true }); return false; }
            state = state with { ViewerHash = Hash(key), Enabled = enabled, AllowAdministration = false, OpenFirewall = false, LegacyMigrated = true };
            if (enabled) state = WithCertificate(state);
            Save(state);
            return true;
        }
    }
    public AgentAccessRole Authenticate(string key)
    {
        if (key.Length == 0 || key.Length > 512) return AgentAccessRole.None;
        lock (_gate)
        {
            var state = Read();
            if (!state.Enabled) return AgentAccessRole.None;
            if (state.AllowAdministration && Matches(key, state.AdminHash)) return AgentAccessRole.Administrator;
            return Matches(key, state.ViewerHash) ? AgentAccessRole.Viewer : AgentAccessRole.None;
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Matches(string value, string? hash) => hash is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(hash));
    public RemoteAccessStatus Status()
    {
        lock (_gate)
        {
            var state = Read();
            using var cert = Certificate(state);
            return new(state.Enabled, state.AllowAdministration, state.OpenFirewall, state.ViewerHash is not null, state.AdminHash is not null, cert?.GetCertHashString(HashAlgorithmName.SHA256));
        }
    }
    public X509Certificate2 GetCertificate()
    {
        lock (_gate) return Certificate(Read()) ?? throw new IOException("Remote certificate is unavailable.");
    }
    private static X509Certificate2? Certificate(State state)
    {
        if (state.ProtectedPfx is null) return null;
        var plain = SecretProtector.Unprotect(state.ProtectedPfx);
        if (plain.Length == 0) throw new IOException("Remote certificate cannot be read by this service account.");
        // Windows Schannel requires a temporary user key container for server TLS; it is removed on disposal.
        return new X509Certificate2(Convert.FromBase64String(plain), (string?)null,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    }
    private static State WithCertificate(State state)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=MonitorAgent", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(Environment.MachineName); san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        return state with { ProtectedPfx = SecretProtector.Protect(Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12))) };
    }
    public RemoteAccessResult Apply(RemoteAccessAction action)
    {
        lock (_gate)
        {
            var state = Read(); string? revealed = null;
            switch (action.Action)
            {
                case "configure":
                    if (action.Enabled && state.ViewerHash is null) throw new ArgumentException("Create a viewer key before enabling remote access.");
                    if (action.AllowAdministration && state.AdminHash is null) throw new ArgumentException("Create a separate admin key before enabling remote administration.");
                    state = state with { Enabled = action.Enabled, AllowAdministration = action.AllowAdministration, OpenFirewall = action.Enabled && action.OpenFirewall };
                    if (state.Enabled && state.ProtectedPfx is null) state = WithCertificate(state);
                    break;
                case "create-viewer-key": case "create-admin-key":
                    revealed = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    state = action.Action == "create-viewer-key" ? state with { ViewerHash = Hash(revealed) } : state with { AdminHash = Hash(revealed) };
                    break;
                case "revoke-viewer-key": state = state with { ViewerHash = null, Enabled = false, OpenFirewall = false }; break;
                case "revoke-admin-key": state = state with { AdminHash = null, AllowAdministration = false }; break;
                case "regenerate-certificate": state = WithCertificate(state); break;
                case "import-certificate":
                    if (string.IsNullOrWhiteSpace(action.Pfx) || action.Pfx.Length > 2 * 1024 * 1024) throw new ArgumentException("Select a PFX certificate within the size limit.");
                    using (var certificate = new X509Certificate2(Convert.FromBase64String(action.Pfx), action.PfxPassword, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable))
                    {
                        if (!certificate.HasPrivateKey || certificate.NotBefore > DateTime.Now || certificate.NotAfter <= DateTime.Now)
                            throw new ArgumentException("The PFX needs a private key and a current validity period.");
                        state = state with { ProtectedPfx = SecretProtector.Protect(Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12))) };
                    }
                    break;
                default: throw new ArgumentException("Unknown remote access action.");
            }
            Save(state);
            return new(Status(), revealed);
        }
    }
}
