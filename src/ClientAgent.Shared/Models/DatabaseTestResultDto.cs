namespace ClientAgent.Shared.Models;

/// <summary>The result of trying a database connection from the service.</summary>
public sealed record DatabaseTestResultDto(bool Success, string Message);
