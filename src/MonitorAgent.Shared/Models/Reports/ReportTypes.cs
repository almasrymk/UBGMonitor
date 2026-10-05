namespace MonitorAgent.Shared.Models.Reports;

public sealed record ReportTypeInfo(string Id, string Title, string Description);

/// <summary>Something the details report can be about.</summary>
/// <param name="Id">"cpu", "ram", "disk:C:", "internet", "point:{MonitorPointId}", or "all", "all:disks", "all:points".</param>
/// <param name="Group">"All", "Device", "Disks", "Network" or "Monitor points", for grouping the list.</param>
public sealed record ReportSubject(string Id, string Title, string Group);

public static class ReportTypes
{
    public const string Summary = "summary";
    public const string Details = "details";
    public const string Incidents = "incidents";
    public const string Availability = "availability";
    public const string Health = "health";
    public const string Compliance = "compliance";
    public const string Internet = "internet";
    public const string Settings = "settings";
    public const string Applications = "applications";
    public const string Inventory = "inventory";

    public static IReadOnlyList<ReportTypeInfo> All { get; } =
    [
        new(Summary, "Summary", "One page with the most important numbers of every report."),
        new(Details, "Details", "Everything about one thing over the period (the processor, the memory, a disk, the internet or a monitor point), or about all of them."),
        new(Incidents, "Problems & Warnings", "Every problem and warning: when it started, when it was resolved and how long it lasted."),
        new(Availability, "Availability", "How much of the time each monitor point, the network and the internet were working."),
        new(Health, "Device Health", "CPU, RAM, temperature, disk activity and disk space over time, with a forecast of when each disk fills up."),
        new(Compliance, "Specifications Compliance", "Whether this device meets the Device Specifications in Settings."),
        new(Internet, "Internet Quality", "Speed tests, ping, packet loss, Wi-Fi signal, data usage and disconnections."),
        new(Settings, "Settings Changes", "Every change saved in Settings, and when."),
        new(Applications, "Applications Changes", "Programs installed, removed or updated, users added or signing in and out, and services that stopped, failed or recovered."),
        new(Inventory, "Device Inventory", "The full hardware, operating system, network and monitor points list of this device.")
    ];

    public static ReportTypeInfo? Find(string id) =>
        All.FirstOrDefault(type => string.Equals(type.Id, id, StringComparison.OrdinalIgnoreCase));

    public const string CpuSubject = "cpu";
    public const string RamSubject = "ram";
    public const string InternetSubject = "internet";
    public const string DiskSubjectPrefix = "disk:";
    public const string PointSubjectPrefix = "point:";

    /// <summary>Every subject in one details report.</summary>
    public const string AllSubject = "all";
    public const string AllDisksSubject = "all:disks";
    public const string AllPointsSubject = "all:points";

    public static bool IsAllSubject(string? id) =>
        id is AllSubject or AllDisksSubject or AllPointsSubject;
}
