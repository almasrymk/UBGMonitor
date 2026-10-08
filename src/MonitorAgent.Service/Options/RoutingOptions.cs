namespace MonitorAgent.Service.Options;

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public string MadkhalServerUrl { get; set; } = string.Empty;

    public string CentralApiUrl { get; set; } = string.Empty;

    public int MadkhalTimeoutSeconds { get; set; } = 5;

    public int CentralTimeoutSeconds { get; set; } = 15;
}
