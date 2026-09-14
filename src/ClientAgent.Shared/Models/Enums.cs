namespace ClientAgent.Shared.Models;

public enum EventType
{
    ResourceThreshold = 0,
    Connectivity = 1,
    DatabaseStatus = 2,
    MadkhalAvailability = 3,
    Heartbeat = 4,
    Recovery = 5,
    Configuration = 6
}

public enum Severity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum EventStatus
{
    Unknown = 0,
    Up = 1,
    Down = 2,
    Ok = 3,
    Warning = 4,
    Critical = 5
}

public enum MonitorPointType
{
    Resource = 0,
    Device = 1,
    Database = 2,
    Madkhal = 3,
    Agent = 4
}

public enum Destination
{
    Madkhal = 0,
    Central = 1,
    Local = 2
}
