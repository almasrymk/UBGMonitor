using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace MonitorAgent.UI.Services;
public static class CertificateTrust
{
    public static string Fingerprint(X509Certificate certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public static bool Accept(X509Certificate? certificate, SslPolicyErrors errors, string? pin)
    {
        if (certificate is null) return false;
        using var parsed = new X509Certificate2(certificate);
        if (parsed.NotBefore > DateTime.Now || parsed.NotAfter <= DateTime.Now) return false;
        return pin is null ? errors == SslPolicyErrors.None : CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Fingerprint(certificate)), System.Text.Encoding.ASCII.GetBytes(pin.ToUpperInvariant()));
    }
}
