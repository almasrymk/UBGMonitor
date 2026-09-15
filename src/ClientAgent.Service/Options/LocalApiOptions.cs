namespace ClientAgent.Service.Options;

public sealed class LocalApiOptions
{
    public const string SectionName = "LocalApi";

    public int Port { get; set; } = 5050;
}
