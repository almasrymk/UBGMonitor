namespace ClientAgent.Shared.Models;

public sealed class HardwareResponseDto
{
    public List<HardwareLevelDto> Levels { get; set; } = [];

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
