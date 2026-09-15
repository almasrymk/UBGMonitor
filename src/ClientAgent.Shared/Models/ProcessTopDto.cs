namespace ClientAgent.Shared.Models;

public sealed class ProcessTopDto
{
    public string Name { get; init; } = string.Empty;

    public int Pid { get; init; }

    public double Value { get; init; }

    public string Unit { get; init; } = string.Empty;
}
