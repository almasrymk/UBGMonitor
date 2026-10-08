using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MonitorAgent.Service.Platform;
using Microsoft.Win32;

namespace MonitorAgent.Service.Licensing;

/// <summary>
/// The device id sent to the license server. It is a hash of the operating system's machine id, so it stays the same
/// after a restart or reinstall, differs between products, and does not reveal the raw machine id.
/// </summary>
public static partial class DeviceFingerprint
{
    private static readonly object Gate = new();
    private static string? _cached;

    public static string Get(string productCode)
    {
        lock (Gate)
        {
            return _cached ??= Hash(productCode, ReadMachineId() ?? StoredRandomId());
        }
    }

    public static string Hash(string productCode, string machineId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"MonitorAgent|{productCode.ToUpperInvariant()}|{machineId.Trim().ToLowerInvariant()}"));
        return "ma-" + Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private static string? ReadMachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return NonEmpty(key?.GetValue("MachineGuid") as string);
            }

            if (OperatingSystem.IsMacOS())
            {
                var output = Command.Run("ioreg", "-rd1 -c IOPlatformExpertDevice")?.Output ?? string.Empty;
                return NonEmpty(PlatformUuid().Match(output).Groups[1].Value);
            }

            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                if (File.Exists(path) && NonEmpty(File.ReadAllText(path)) is { } id)
                {
                    return id;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return null;
    }

    /// <summary>For systems without a machine id: a random id kept next to the agent's other machine-wide files.</summary>
    private static string StoredRandomId()
    {
        var path = Path.Combine(AgentPaths.StateFolder, "device.id");
        try
        {
            if (File.Exists(path) && NonEmpty(File.ReadAllText(path)) is { } existing)
            {
                return existing;
            }

            Directory.CreateDirectory(AgentPaths.StateFolder);
            var created = Guid.NewGuid().ToString("N");
            MonitorAgent.Shared.Security.PrivateFile.WriteAllText(path, created);
            return created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Environment.MachineName;
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex PlatformUuid();
}
