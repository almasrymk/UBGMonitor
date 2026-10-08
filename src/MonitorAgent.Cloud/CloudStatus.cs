namespace MonitorAgent.Cloud;

/// <summary>What the connector is doing, for the local API, the desktop app and the logs (docs/agent-integration.md).</summary>
public sealed class CloudStatus
{
    private readonly object _gate = new();

    public string State { get; private set; } = "Disabled";

    public string? LastError { get; private set; }

    public string? SessionId { get; private set; }

    public long LastAcknowledged { get; private set; }

    public DateTimeOffset? ConnectedSince { get; private set; }

    public DateTimeOffset? LastChange { get; private set; }

    public void Set(string state, string? error = null)
    {
        lock (_gate)
        {
            State = state;
            LastError = error;
            LastChange = DateTimeOffset.UtcNow;
            if (state != "Connected")
            {
                SessionId = null;
                ConnectedSince = null;
            }
        }
    }

    public void Connected(string sessionId)
    {
        lock (_gate)
        {
            State = "Connected";
            LastError = null;
            SessionId = sessionId;
            ConnectedSince = DateTimeOffset.UtcNow;
            LastChange = ConnectedSince;
        }
    }

    public void Acknowledged(long sequence)
    {
        lock (_gate)
        {
            LastAcknowledged = Math.Max(LastAcknowledged, sequence);
        }
    }
}
