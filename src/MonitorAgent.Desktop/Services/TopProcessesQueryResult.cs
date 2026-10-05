using MonitorAgent.UI.Models;

namespace MonitorAgent.UI.Services;

public sealed record TopProcessesQueryResult(IReadOnlyList<ProcessItem> Items, string? Error)
{
    public static TopProcessesQueryResult Ok(IReadOnlyList<ProcessItem> items)
        => new(items, null);

    public static TopProcessesQueryResult Fail(string error)
        => new([], error);
}
