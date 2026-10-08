using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Config;

public static class DatabaseTestResolver
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    public static DatabaseLogin Resolve(DatabaseTestRequest request, JsonObject stored)
    {
        if (request.Login is not null && SecretProtector.IsProtected(request.Login.Password))
            throw new ArgumentException("Protected passwords from clients are not accepted.");
        DatabaseLogin? saved = null;
        if (!string.IsNullOrWhiteSpace(request.MonitorPointId))
        {
            var points = stored["MonitorPoints"]?.Deserialize<List<MonitorPoint>>(Json) ?? [];
            saved = points.FirstOrDefault(p => p.MonitorPointId == request.MonitorPointId)?.Database;
            if (saved is null) throw new ArgumentException("The stored database monitor point does not exist.");
        }
        if (request.Login is null)
        {
            var resolved = saved?.Copy() ?? throw new ArgumentException("A monitorPointId or login is required.");
            if (request.Verify) resolved.TlsMode = DatabaseTlsMode.Verify;
            return resolved;
        }
        var login = request.Login.Copy();
        if (string.IsNullOrEmpty(login.Password) && saved is not null)
        {
            var suppliedNode = JsonSerializer.SerializeToNode(login, Json)!.AsObject();
            var savedNode = JsonSerializer.SerializeToNode(saved, Json)!.AsObject();
            if (!SettingsContract.SameTarget(suppliedNode, savedNode)) throw new ArgumentException("Database target changed; enter the password again.");
            login.Password = saved.Password;
        }
        else if (login.HasPassword && string.IsNullOrEmpty(login.Password)) throw new ArgumentException("Select the saved monitor point or enter a password.");
        if (!Enum.IsDefined(login.TlsMode) || !Enum.IsDefined(login.Engine) || login.Port is < 1 or > 65535) throw new ArgumentException("Database engine, TLS mode or port is invalid.");
        if (request.Verify) login.TlsMode = DatabaseTlsMode.Verify;
        return login;
    }
}
