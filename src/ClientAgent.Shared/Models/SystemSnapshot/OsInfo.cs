namespace ClientAgent.Shared.Models;

public sealed class OsInfo
{
    public string Name { get; init; } = "Unknown";

    public string Version { get; init; } = "Unknown";

    public string Build { get; init; } = "Unknown";

    public string Architecture { get; init; } = "Unknown";

    public DateTime? InstallDate { get; init; }

    public DateTime? LastBoot { get; init; }

    public TimeSpan Uptime { get; init; }

    public string Timezone { get; init; } = TimeZoneInfo.Local.DisplayName;

    public string Locale { get; init; } = "Unknown";

    public string SystemType { get; init; } = "Unknown";
}
