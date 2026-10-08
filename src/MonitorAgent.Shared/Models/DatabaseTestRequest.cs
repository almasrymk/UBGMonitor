namespace MonitorAgent.Shared.Models;

public sealed record DatabaseTestRequest(string? MonitorPointId = null, DatabaseLogin? Login = null);
