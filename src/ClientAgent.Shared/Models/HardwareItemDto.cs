namespace ClientAgent.Shared.Models;

public sealed class HardwareItemDto
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Status { get; set; } = "Red";
}
