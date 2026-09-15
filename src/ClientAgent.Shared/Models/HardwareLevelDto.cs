namespace ClientAgent.Shared.Models;

public sealed class HardwareLevelDto
{
    public int Level { get; set; }

    public string Title { get; set; } = string.Empty;

    public List<HardwareItemDto> Items { get; set; } = [];

    public int ItemCount { get; set; }
}
