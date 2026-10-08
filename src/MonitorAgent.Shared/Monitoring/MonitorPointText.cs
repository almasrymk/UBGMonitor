using MonitorAgent.Shared.Models;

namespace MonitorAgent.Shared.Monitoring;

/// <summary>English labels for a monitor point's settings, shared by the app screens and the reports.</summary>
public static class MonitorPointText
{
    public static string Type(MonitorPointType type) => type switch
    {
        MonitorPointType.Website => "Website/API",
        MonitorPointType.Device => "Device",
        MonitorPointType.Application => "Application",
        MonitorPointType.Database => "Database",
        _ => type.ToString()
    };

    public static string DeviceKind(GarageDeviceKind? kind) => kind switch
    {
        null => string.Empty,
        GarageDeviceKind.CardReader => "Card Reader",
        _ => kind.Value.ToString()
    };

    public static string Alert(MonitorPointAlert alert) => alert switch
    {
        MonitorPointAlert.Warning => "Warning",
        MonitorPointAlert.Unknown => "Unknown",
        _ => "Problem"
    };

    public static string Engine(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => "PostgreSQL",
        DatabaseEngine.MySql => "MySQL",
        _ => "SQL Server"
    };

    /// <summary>What is checked: the address, the program path, or the database server and name.</summary>
    public static string Target(MonitorPoint point)
    {
        if (point.Type == MonitorPointType.Database)
        {
            var login = point.Database;
            return login is null || string.IsNullOrWhiteSpace(login.Server)
                ? "Not configured"
                : $"{Engine(login.Engine)} - {login.Server}{(login.Port > 0 ? $":{login.Port}" : string.Empty)} / {login.Database}" + (login.TlsMode == DatabaseTlsMode.Compatibility ? " · server identity not verified" : "");
        }

        return point.Address;
    }

    public static string ResponseTime(double? milliseconds) => milliseconds switch
    {
        null => "-",
        >= 1000 => $"{milliseconds / 1000:0.00} s",
        _ => $"{milliseconds:0} ms"
    };
}
