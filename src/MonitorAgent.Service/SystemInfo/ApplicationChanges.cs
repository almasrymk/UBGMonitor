using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.SystemInfo;

/// <param name="Area">"Programs", "Users" or "Services".</param>
/// <param name="Severity">"Critical", "Warning" or "Info".</param>
/// <param name="ServiceName">For a service that broke or recovered: its name, to open or close its problem.</param>
/// <param name="Recovered">The service is working again.</param>
public sealed record ApplicationChange(string Area, string Severity, string Text, string? ServiceName = null, bool Recovered = false);

/// <summary>What changed between two readings of the installed programs, the user accounts or the services.</summary>
public static class ApplicationChanges
{
    public const string ProgramsArea = "Programs";
    public const string UsersArea = "Users";
    public const string ServicesArea = "Services";
    public const string ServiceIssuePrefix = "service:";

    private const string Info = "Info";
    private const string Warning = "Warning";
    private const string Critical = "Critical";

    /// <summary>Programs installed, uninstalled or updated (starting and closing them is not a change of the device).</summary>
    public static List<ApplicationChange> ComparePrograms(IReadOnlyList<InstalledProgram> before, IReadOnlyList<InstalledProgram> after)
    {
        var old = Versions(before);
        var current = Versions(after);
        var changes = new List<ApplicationChange>();
        foreach (var (name, version) in current.Where(pair => !old.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new(ProgramsArea, Info, $"Program installed: {name}{(version.Length > 0 ? $" {version}" : string.Empty)}"));
        }

        foreach (var name in old.Keys.Where(name => !current.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new(ProgramsArea, Info, $"Program uninstalled: {name}"));
        }

        foreach (var (name, version) in current.Where(pair => old.TryGetValue(pair.Key, out var was) && was != pair.Value))
        {
            changes.Add(new(ProgramsArea, Info, $"Program updated: {name} {Text(old[name])} -> {Text(version)}"));
        }

        return changes;
    }

    /// <summary>Accounts added, removed, enabled, disabled or made administrators, and users signing in, out or disconnecting.</summary>
    public static List<ApplicationChange> CompareUsers(IReadOnlyList<UserAccountDto> before, IReadOnlyList<UserAccountDto> after)
    {
        var old = ByKey(before, UserLabel);
        var current = ByKey(after, UserLabel);
        var changes = new List<ApplicationChange>();
        foreach (var (name, user) in current.Where(pair => !old.ContainsKey(pair.Key)))
        {
            changes.Add(new(UsersArea, user.IsLocal ? Warning : Info, $"User account added: {name}{(user.IsAdmin ? " (administrator)" : string.Empty)}"));
            if (user.IsSignedIn)
            {
                changes.Add(new(UsersArea, Info, $"{name} signed in{Session(user)}"));
            }
        }

        foreach (var name in old.Keys.Where(name => !current.ContainsKey(name)))
        {
            changes.Add(new(UsersArea, Info, $"User account removed: {name}"));
        }

        foreach (var (name, user) in current.Where(pair => old.ContainsKey(pair.Key)))
        {
            var was = old[name];
            if (was.Enabled != user.Enabled)
            {
                changes.Add(new(UsersArea, Info, $"User account {(user.Enabled ? "enabled" : "disabled")}: {name}"));
            }

            if (was.IsAdmin != user.IsAdmin)
            {
                changes.Add(user.IsAdmin
                    ? new(UsersArea, Warning, $"{name} became an administrator")
                    : new(UsersArea, Info, $"{name} is no longer an administrator"));
            }

            if (was.IsSignedIn != user.IsSignedIn)
            {
                changes.Add(new(UsersArea, Info, user.IsSignedIn ? $"{name} signed in{Session(user)}" : $"{name} signed out"));
            }
            else if (user.IsSignedIn && was.SessionState != user.SessionState
                     && (IsDisconnected(was.SessionState) || IsDisconnected(user.SessionState)))
            {
                changes.Add(new(UsersArea, Info, IsDisconnected(user.SessionState)
                    ? $"{name} disconnected (still signed in)"
                    : $"{name} reconnected{Session(user)}"));
            }
        }

        return changes;
    }

    /// <summary>
    /// Services installed, removed or with a new start type, and services that broke or recovered. A service that only starts
    /// and stops on demand is not reported (Windows does that all day), nor one still starting or stopping.
    /// </summary>
    /// <returns>The changes, and the reading to compare the next one with (keeping the last settled state of services in between).</returns>
    public static (List<ApplicationChange> Changes, IReadOnlyList<SystemServiceDto> Settled) CompareServices(
        IReadOnlyList<SystemServiceDto> before, IReadOnlyList<SystemServiceDto> after)
    {
        var old = ByKey(before, s => s.Name);
        var settled = new List<SystemServiceDto>(after.Count);
        var changes = new List<ApplicationChange>();
        foreach (var service in after)
        {
            old.TryGetValue(service.Name, out var was);
            if (IsTransitional(service.State))
            {
                settled.Add(was ?? service);
                continue;
            }

            settled.Add(service);
            var label = ServiceLabel(service);
            if (was is null)
            {
                changes.Add(new(ServicesArea, Info, $"Service installed: {label} ({service.State}, {service.StartMode})"));
                continue;
            }

            if (!string.Equals(was.StartMode, service.StartMode, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new(ServicesArea, Info, $"Service start type changed: {label} {Text(was.StartMode)} -> {Text(service.StartMode)}"));
            }

            var problem = Problem(service);
            var wasProblem = Problem(was);
            if (problem is not null && (wasProblem is null || (problem.Value.Severity == Critical && wasProblem.Value.Severity != Critical)))
            {
                changes.Add(new(ServicesArea, problem.Value.Severity, $"Service {Verb(service.State)}: {label} - {problem.Value.Reason}", service.Name));
            }
            else if (problem is null && wasProblem is not null)
            {
                changes.Add(new(ServicesArea, Info, $"Service recovered: {label} - now {service.State.ToLowerInvariant()}", service.Name, Recovered: true));
            }
        }

        var current = new HashSet<string>(after.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var service in before.Where(s => !current.Contains(s.Name)))
        {
            changes.Add(new(ServicesArea, Info, $"Service removed: {ServiceLabel(service)}", service.Name, Recovered: true));
        }

        return (changes, settled);
    }

    /// <summary>Why the service counts as broken, or null when it is fine (running, finished, or stopped and only started on demand).</summary>
    public static (string Severity, string Reason)? Problem(SystemServiceDto service)
    {
        if (service.Health == Critical)
        {
            return (Critical, service.Problem ?? service.State);
        }

        if (service.Health == Warning)
        {
            return (Warning, service.Problem ?? service.State);
        }

        // Linux marks an enabled unit that stopped as Unknown; it is meant to be running like an automatic Windows service.
        return service.State == "Stopped" && service.StartMode is "Automatic" or "Enabled"
            ? (Warning, "Set to start with the system but is not running")
            : null;
    }

    /// <summary>"Display name (name)", unless the display name already carries the name (as Google's updater services do).</summary>
    public static string ServiceLabel(SystemServiceDto service) =>
        string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name
            : service.DisplayName.Contains(service.Name, StringComparison.OrdinalIgnoreCase) ? service.DisplayName
            : $"{service.DisplayName} ({service.Name})";

    private static bool IsTransitional(string state) =>
        state.Contains("Pending", StringComparison.OrdinalIgnoreCase) || state is "Starting" or "Stopping" or "Reloading";

    private static bool IsDisconnected(string? state) => string.Equals(state, "Disconnected", StringComparison.OrdinalIgnoreCase);

    private static string Verb(string state) => state switch
    {
        "Stopped" => "stopped",
        "Failed" => "failed",
        "Paused" => "paused",
        "Running" => "problem",
        _ => state.ToLowerInvariant()
    };

    private static string UserLabel(UserAccountDto user) =>
        string.IsNullOrWhiteSpace(user.Domain) || user.IsLocal ? user.UserName : $"{user.Domain}\\{user.UserName}";

    private static string Session(UserAccountDto user)
    {
        var how = string.IsNullOrWhiteSpace(user.SessionType) ? null : user.SessionType;
        var from = string.IsNullOrWhiteSpace(user.ClientName) ? null : $"from {user.ClientName}";
        var details = string.Join(' ', new[] { how, from }.OfType<string>());
        return details.Length == 0 ? string.Empty : $" ({details})";
    }

    /// <summary>Each program name with its versions; the same program can be listed twice (32 and 64 bit, or per user).</summary>
    private static Dictionary<string, string> Versions(IReadOnlyList<InstalledProgram> programs) =>
        programs
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(p => p.Version?.Trim()).Where(v => !string.IsNullOrEmpty(v)).Distinct().Order()),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, T> ByKey<T>(IEnumerable<T> items, Func<T, string> key)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            result.TryAdd(key(item), item);
        }

        return result;
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
}
