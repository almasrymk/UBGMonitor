using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ClientAgent.Shared.Security;

[SupportedOSPlatform("windows")]
public static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClientAgent.Database.v1");

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        if (plain.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return plain;
        }

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
            var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
