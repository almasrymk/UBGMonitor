namespace MonitorAgent.Service.Platform;

/// <summary>
/// Where the agent keeps its files on each system. Windows keeps the folders it has always used, so an update
/// finds the existing data; Linux and macOS use their standard system folders because the install folder is read-only.
/// Setting MONITORAGENT_HOME moves the data and logs (for running a test copy without root rights).
/// </summary>
public static class AgentPaths
{
    private static readonly string? Home = Environment.GetEnvironmentVariable("MONITORAGENT_HOME") is { Length: > 0 } home
        ? Path.GetFullPath(home)
        : null;

    /// <summary>The machine-wide folder for config.json, local.db and the secret key.</summary>
    public static string StateFolder => Home
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonitorAgent")
            : OperatingSystem.IsMacOS()
                ? "/Library/Application Support/MonitorAgent"
                : "/var/lib/monitoragent");

    /// <summary>The readings, problems and reports.db behind the reports.</summary>
    public static string DataFolder => Home is not null || !OperatingSystem.IsWindows()
        ? Path.Combine(StateFolder, "Data")
        : Path.Combine(AppContext.BaseDirectory, "Data");

    public static string ReportsFolder => Home is not null || !OperatingSystem.IsWindows()
        ? Path.Combine(StateFolder, "Reports")
        : Path.Combine(AppContext.BaseDirectory, "Reports");

    public static string LogFolder => Home is not null
        ? Path.Combine(Home, "logs")
        : OperatingSystem.IsWindows()
            ? Path.Combine(StateFolder, "logs")
            : OperatingSystem.IsMacOS()
                ? "/Library/Logs/MonitorAgent"
                : "/var/log/monitoragent";

    public static string DefaultLogPath => Path.Combine(LogFolder, "agent-.log");
}
