using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Config;

public static class SettingsContract
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private static JsonNode? Field(JsonObject node, string name) => node.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static JsonNode Typed<T>(JsonNode? input) where T : class, new() => JsonSerializer.SerializeToNode(input?.Deserialize<T>(Json) ?? new T(), Json)!;
    private static JsonObject Normalize(JsonObject input) => new()
    {
        ["General"] = Typed<GeneralRuntimeSettings>(Field(input, "General")),
        ["DeviceSpec"] = Typed<DeviceSpecSettings>(Field(input, "DeviceSpec")),
        ["MonitorPoints"] = JsonSerializer.SerializeToNode(Field(input, "MonitorPoints")?.Deserialize<List<MonitorPoint>>(Json) ?? [], Json),
        ["Conditions"] = new JsonArray((Field(input, "Conditions") as JsonArray ?? []).OfType<JsonObject>().Select(c => (JsonNode)new JsonObject(
            c.Where(p => new[] { "Name", "TargetId", "Rule", "Threshold", "Severity", "Enabled" }.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())))).ToArray())
    };

    public static JsonObject PublicSettings(JsonObject stored)
    {
        var result = Normalize(stored);
        var general = result["General"]!.AsObject();
        general["HasRemoteAccessKey"] = !string.IsNullOrEmpty(general["RemoteAccessKey"]?.GetValue<string>());
        general.Remove("RemoteAccessKey");
        foreach (var point in result["MonitorPoints"]!.AsArray().OfType<JsonObject>())
        {
            // Metadata is extensible; it is not part of the public settings contract.
            point.Remove("Metadata");
            if (point["Database"] is JsonObject login)
            {
                login["HasPassword"] = !string.IsNullOrEmpty(login["Password"]?.GetValue<string>());
                login.Remove("Password");
            }
        }
        RemoveProtectedValues(result);
        return result;
    }

    public static JsonObject Merge(JsonObject submitted, JsonObject stored)
    {
        if (Field(submitted, "Conditions") is JsonNode conditions &&
            (conditions is not JsonArray array || array.Any(c => c is not JsonObject)))
            throw new ArgumentException("Conditions must contain objects only.");
        var result = Normalize(submitted);
        var before = Normalize(stored);
        var general = result["General"]!.AsObject();
        var previousGeneral = before["General"]!.AsObject();
        general["RemoteAccessKey"] = previousGeneral["RemoteAccessKey"]?.DeepClone() ?? JsonValue.Create("");
        var address = general["ServiceListenAddress"]!.GetValue<string>();
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !(IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(ip))))
            throw new ArgumentException("General.ServiceListenAddress must be an IPv4 address of this computer.");
        var port = general["ServicePort"]!.GetValue<int>();
        if (port is < 0 or > 65535) throw new ArgumentException("General.ServicePort must be 1-65535 (0 keeps the legacy default).");
        foreach (var field in general.Where(p => p.Key.EndsWith("Interval") || p.Key.EndsWith("IntervalSeconds")))
        {
            var value = field.Value!.GetValue<int>();
            if (value < (field.Key == "SpeedTestIntervalSeconds" ? 0 : 1) || value > 86400)
                throw new ArgumentException($"General.{field.Key} is outside the supported interval range.");
        }
        if (general["DataRetentionDays"]!.GetValue<int>() is < 30 or > 365) throw new ArgumentException("General.DataRetentionDays must be 30-365.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incoming = (Field(submitted, "MonitorPoints") as JsonArray ?? []).OfType<JsonObject>().ToList();
        var oldPoints = before["MonitorPoints"]!.AsArray().OfType<JsonObject>().ToList();
        if (incoming.Count > 1000) throw new ArgumentException("MonitorPoints exceeds its size limit.");
        foreach (var point in result["MonitorPoints"]!.AsArray().OfType<JsonObject>())
        {
            var id = point["MonitorPointId"]!.GetValue<string>();
            if (!Enum.TryParse<MonitorPointType>(point["Type"]!.GetValue<string>(), out var type) || !Enum.IsDefined(type)) throw new ArgumentException("MonitorPoints.Type is invalid.");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("MonitorPoints must have unique non-empty IDs.");
            if (point["IntervalSeconds"]!.GetValue<int>() is < 1 or > 86400) throw new ArgumentException("MonitorPoints.IntervalSeconds is outside the supported range.");
            if (point["Type"]!.GetValue<string>() == "Website")
            {
                var value = point["Address"]!.GetValue<string>();
                if (!value.Contains("://")) value = "https://" + value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new ArgumentException("Website addresses must use HTTP or HTTPS.");
            }
            var rawPoint = incoming.First(p => Field(p, "MonitorPointId")?.GetValue<string>() == id);
            // Preserve private metadata for every point type, not only databases.
            if (Field(rawPoint, "Metadata") is null)
                point["Metadata"] = oldPoints.FirstOrDefault(p => p["MonitorPointId"]!.GetValue<string>() == id)?["Metadata"]?.DeepClone() ?? new JsonObject();
            if (point["Database"] is not JsonObject login) continue;
            if (!Enum.TryParse<DatabaseEngine>(login["Engine"]!.GetValue<string>(), out var engine) || !Enum.IsDefined(engine)) throw new ArgumentException("Database.Engine is invalid.");
            if (!Enum.TryParse<DatabaseTlsMode>(login["TlsMode"]!.GetValue<string>(), out var tls) || !Enum.IsDefined(tls)) throw new ArgumentException("Database.TlsMode is invalid.");
            if (login["Port"]!.GetValue<int>() is < 1 or > 65535) throw new ArgumentException("Database.Port must be 1-65535.");
            var rawLogin = Field(rawPoint, "Database") as JsonObject;
            var password = rawLogin is null ? null : Field(rawLogin, "Password")?.GetValue<string>();
            if (SecretProtector.IsProtected(password)) throw new ArgumentException("Database.Password must be newly entered text, not a protected blob.");
            var old = oldPoints.FirstOrDefault(p => p["MonitorPointId"]!.GetValue<string>() == id)?["Database"] as JsonObject;
            if (password is null || password.Length == 0 && rawLogin is not null && Field(rawLogin, "HasPassword")?.GetValue<bool>() == true)
            {
                if (old is not null && !SameTarget(login, old) && !string.IsNullOrEmpty(old["Password"]?.GetValue<string>()))
                    throw new ArgumentException("Database target changed; enter the password again.");
                login["Password"] = old?["Password"]?.DeepClone() ?? JsonValue.Create("");
            }
            login.Remove("HasPassword");
        }
        ValidateStrings(result);
        return result;
    }

    public static bool SameTarget(JsonObject left, JsonObject right) =>
        new[] { "Engine", "Server", "Port", "Database", "Username", "IntegratedSecurity" }
            .All(name => JsonNode.DeepEquals(left[name], right[name]));

    private static void RemoveProtectedValues(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var field in obj.ToList())
            {
                if (field.Value is JsonValue value && value.TryGetValue<string>(out var text) && SecretProtector.IsProtected(text)) obj.Remove(field.Key);
                else if (field.Value is not null) RemoveProtectedValues(field.Value);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child is not null) RemoveProtectedValues(child);
    }

    private static void ValidateStrings(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var field in obj)
            {
                if (field.Value is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > (field.Key == "Icon" ? 1024 * 1024 : 8192))
                    throw new ArgumentException($"{field.Key} exceeds its size limit.");
                if (field.Value is not null) ValidateStrings(field.Value);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child is not null) ValidateStrings(child);
    }
}
