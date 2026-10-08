using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.UI.Services;

// Local administrator display cache. The service continues to store hashes only.
public sealed class RemoteKeyDisplayStore
{
    private readonly string _path;
    public RemoteKeyDisplayStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorAgent", "remote-key-display.json");
    private Dictionary<string, string> Read()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string role, string key)
    {
        var values = Read();
        values[role] = SecretProtector.Protect(key);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        PrivateFile.WriteAllText(_path, JsonSerializer.Serialize(values));
    }
    public string Load(string role, string? expectedHash)
    {
        if (expectedHash is null || !Read().TryGetValue(role, out var value) || !SecretProtector.IsProtected(value)) return "";
        var key = SecretProtector.Unprotect(value);
        return key.Length > 0 && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) == expectedHash ? key : "";
    }
}
