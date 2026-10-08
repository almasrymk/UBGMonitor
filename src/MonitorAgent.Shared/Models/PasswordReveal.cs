namespace MonitorAgent.Shared.Models;
public sealed record PasswordRevealRequest(string MonitorPointId);
public sealed record PasswordRevealResult(string Password);
