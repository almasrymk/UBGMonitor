namespace ClientAgent.Service.Options;

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public string MadkhalServerUrl { get; set; } = "http://madkhal.local:8080";

    public string CentralApiUrl { get; set; } = "https://api.central.local";

    public int MadkhalTimeoutSeconds { get; set; } = 5;

    public int CentralTimeoutSeconds { get; set; } = 15;
}
