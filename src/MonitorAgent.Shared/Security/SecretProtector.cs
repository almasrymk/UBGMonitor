using System.Security.Cryptography;
using System.Text;

namespace MonitorAgent.Shared.Security;

/// <summary>
/// Protects saved passwords and keys for this computer. Windows uses DPAPI (machine scope); Linux and macOS use
/// AES-GCM with a random key in a file only root can read (<see cref="KeyFile"/>).
/// </summary>
public static class SecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string AesPrefix = "aes:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClientAgent.Database.v1");
    private static readonly object KeyGate = new();
    private static byte[]? _key;

    /// <summary>The machine key used outside Windows; MONITORAGENT_HOME moves it along with the agent's data.</summary>
    public static string KeyFile { get; set; } = Path.Combine(
        Environment.GetEnvironmentVariable("MONITORAGENT_HOME") is { Length: > 0 } home
            ? home
            : OperatingSystem.IsMacOS() ? "/Library/Application Support/MonitorAgent" : "/var/lib/monitoragent",
        "secret.key");

    public static bool IsProtected(string? value)
        => value is not null && (value.StartsWith(DpapiPrefix, StringComparison.Ordinal) || value.StartsWith(AesPrefix, StringComparison.Ordinal));

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        if (IsProtected(plain))
        {
            return plain;
        }

        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
        {
            return DpapiPrefix + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine));
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var sealedBytes = new byte[NonceSize + TagSize + bytes.Length];
        using (var aes = new AesGcm(MachineKey(create: true)!, TagSize))
        {
            aes.Encrypt(nonce, bytes, sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize), Entropy);
        }

        nonce.CopyTo(sealedBytes, 0);
        return AesPrefix + Convert.ToBase64String(sealedBytes);
    }

    /// <returns>The secret, or an empty string when the value is not protected or was protected on another computer.</returns>
    public static string Unprotect(string? stored)
    {
        try
        {
            if (stored is null)
            {
                return string.Empty;
            }

            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                return OperatingSystem.IsWindows()
                    ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[DpapiPrefix.Length..]), Entropy, DataProtectionScope.LocalMachine))
                    : string.Empty;
            }

            if (stored.StartsWith(AesPrefix, StringComparison.Ordinal) && MachineKey(create: false) is { } key)
            {
                var sealedBytes = Convert.FromBase64String(stored[AesPrefix.Length..]);
                if (sealedBytes.Length < NonceSize + TagSize)
                {
                    return string.Empty;
                }

                var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize), plain, Entropy);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        {
        }

        return string.Empty;
    }

    private static byte[]? MachineKey(bool create)
    {
        lock (KeyGate)
        {
            if (_key is not null)
            {
                return _key;
            }

            if (File.Exists(KeyFile))
            {
                var existing = File.ReadAllBytes(KeyFile);
                if (existing.Length == 32)
                {
                    return _key = existing;
                }
            }

            if (!create)
            {
                return null;
            }

            var key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
            using (var stream = OperatingSystem.IsWindows()
                       ? new FileStream(KeyFile, FileMode.Create, FileAccess.Write)
                       : new FileStream(KeyFile, new FileStreamOptions
                       {
                           Mode = FileMode.Create,
                           Access = FileAccess.Write,
                           UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                       }))
            {
                stream.Write(key);
            }

            return _key = key;
        }
    }
}
