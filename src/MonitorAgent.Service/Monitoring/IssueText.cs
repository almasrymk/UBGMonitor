namespace MonitorAgent.Service.Monitoring;

internal static class IssueText
{
    public static string Stamp(string body)
        => $"{body}{Environment.NewLine}Alert time: {DateTime.Now:HH:mm}";

    public static string ApplicationDown(string name, string? processName)
    {
        var process = string.IsNullOrWhiteSpace(processName) ? string.Empty : $" ({processName})";
        return Stamp($"Important: the application \"{name}\"{process} is not running. Please check it.");
    }

    public static string DeviceDown(string name)
        => Stamp($"Important: the device \"{name}\" is not reachable. Please check it now.");

    public static string WebsiteDown(string name, string address, string detail)
        => Stamp($"Important: the website \"{name}\" is down.{Environment.NewLine}Address: {address}{Environment.NewLine}{detail}");

    public static string WebsiteHttpsOnly(string name, string address, string httpsDetail)
        => Stamp($"Warning: the website \"{name}\" works over HTTP only.{Environment.NewLine}Address: {address}{Environment.NewLine}HTTPS is not available. {httpsDetail}");

    public static string CpuHigh(double usage)
        => Stamp($"Important: CPU usage is very high ({usage:0}%). Please act now.");

    public static string RamHigh(double usage)
        => Stamp($"Important: memory usage is very high ({usage:0}%). Please act now.");

    public static string DiskHigh(string drive, double usage)
        => Stamp($"Important: disk {drive} usage is very high ({usage:0}%). Please act now.");

    public static string MadkhalDown()
        => Stamp("Important: the Madkhal server is not available. Please check the connection now.");

    public static string DatabaseDown()
        => Stamp("Warning: cannot connect to the local database. Please check it now.");

    public static string DatabasePointDown(string name, string engine, string server, string detail)
        => Stamp($"Important: the database \"{name}\" ({engine} / {server}) is not available.{Environment.NewLine}{detail}");
}
