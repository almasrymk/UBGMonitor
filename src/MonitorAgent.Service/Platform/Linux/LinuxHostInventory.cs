using System.Globalization;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>
/// Programs from the launcher entries (.desktop files) of the system and every user, accounts from /etc/passwd and
/// /etc/shadow, sign-ins from systemd-logind (who when it is missing), and services from systemd.
/// </summary>
public sealed class LinuxHostInventory : BasicHostInventory
{
    private const string SearchPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/snap/bin";
    private static readonly string[] SharedFolders = ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/usr/local/bin", "/usr/local/sbin", "/usr/games", "/snap/bin", "/usr/lib", "/usr/libexec"];
    private static readonly string[] AdminGroups = ["sudo", "wheel", "admin"];
    private static readonly TimeSpan ActiveIdleLimit = TimeSpan.FromMinutes(5);

    public override IReadOnlyList<InstalledProgram> ReadPrograms()
    {
        var roots = new List<string>
        {
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications",
            "/root/.local/share/applications"
        };
        roots.AddRange(LinuxFiles.Directories("/home").Select(home => Path.Combine(home, ".local", "share", "applications")));

        var found = new Dictionary<string, InstalledProgram>(StringComparer.Ordinal);
        foreach (var file in roots.SelectMany(root => LinuxFiles.Files(root, "*.desktop")))
        {
            if (ReadDesktopEntry(file) is { } program)
            {
                found.TryAdd(program.Name, program);
            }
        }

        return found.Values.ToList();
    }

    public override IReadOnlyList<UserAccountDto> ReadUsers()
    {
        var shadow = ReadColonFile("/etc/shadow").ToDictionary(fields => fields[0], StringComparer.Ordinal);
        var groups = ReadColonFile("/etc/group").Where(fields => fields.Length >= 4).ToList();
        var adminGids = groups.Where(g => AdminGroups.Contains(g[0])).Select(g => g[2]).ToHashSet();
        var adminMembers = groups.Where(g => AdminGroups.Contains(g[0]))
            .SelectMany(g => g[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var sessions = ReadSessions();
        var logons = UnixLogins.Last("-F -w");
        var users = new List<UserAccountDto>();

        foreach (var fields in ReadColonFile("/etc/passwd").Where(f => f.Length >= 7))
        {
            var (name, gid, gecos, home, shell) = (fields[0], fields[3], fields[4], fields[5], fields[6]);
            if (!int.TryParse(fields[2], out var uid) || !IsPerson(uid, shell))
            {
                continue;
            }

            var secret = shadow.GetValueOrDefault(name);
            var session = sessions.Where(s => s.UserName == name).OrderByDescending(s => s.IsActive).FirstOrDefault();
            var logon = logons.GetValueOrDefault(name);
            users.Add(new UserAccountDto
            {
                UserName = name,
                FullName = gecos.Split(',')[0] is { Length: > 0 } full ? full : null,
                Domain = Environment.MachineName,
                IsLocal = true,
                Enabled = secret is null || !secret[1].StartsWith('!'),
                IsAdmin = uid == 0 || adminMembers.Contains(name) || adminGids.Contains(gid),
                IsSignedIn = session is not null,
                IsActive = session?.IsActive == true,
                SessionState = session?.State,
                SessionType = session?.Type,
                ClientName = session?.Host,
                SignedInUtc = session?.SignedInUtc,
                IdleSeconds = session?.IdleSeconds,
                LastLogonUtc = logon?.LatestUtc ?? session?.SignedInUtc,
                LogonCount = logon?.Count,
                PasswordLastSetUtc = secret is { Length: > 2 } && long.TryParse(secret[2], out var days) && days > 0
                    ? DateTime.UnixEpoch.AddDays(days)
                    : null,
                ProfilePath = home
            });
        }

        return users;
    }

    public override IReadOnlyList<SystemServiceDto> ReadServices()
    {
        var startModes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Command.Run("systemctl", "list-unit-files --type=service --no-legend --no-pager --plain") is { Succeeded: true } files)
        {
            foreach (var words in Lines(files.Output).Select(Words).Where(w => w.Length >= 2))
            {
                startModes[words[0]] = words[1];
            }
        }

        var services = new List<SystemServiceDto>();
        if (Command.Run("systemctl", "list-units --type=service --all --no-legend --no-pager --plain") is not { Succeeded: true } units)
        {
            return services;
        }

        foreach (var line in Lines(units.Output))
        {
            var words = Words(line.TrimStart('●', ' '));
            if (words.Length < 4 || words[1] == "not-found")
            {
                continue;
            }

            var (unit, active, sub) = (words[0], words[2], words[3]);
            var description = string.Join(' ', words.Skip(4));
            var startMode = startModes.GetValueOrDefault(unit, string.Empty);
            var (state, health, problem) = (active, sub) switch
            {
                ("active", "running") => ("Running", "Healthy", (string?)null),
                ("active", "exited") => ("Finished", "Healthy", null),
                ("active", _) => (Capitalize(sub), "Healthy", null),
                ("failed", _) => ("Failed", "Critical", $"Failed ({sub})"),
                ("activating", _) => ("Starting", "Warning", "Starting"),
                ("deactivating", _) => ("Stopping", "Warning", "Stopping"),
                ("reloading", _) => ("Reloading", "Warning", "Reloading"),
                _ => ("Stopped", "Unknown", null)
            };
            services.Add(new SystemServiceDto
            {
                Name = unit.EndsWith(".service", StringComparison.Ordinal) ? unit[..^8] : unit,
                DisplayName = description.Length > 0 ? description : unit,
                Description = description,
                State = state,
                StartMode = Capitalize(startMode),
                Health = health,
                Problem = problem
            });
        }

        return services;
    }

    public override string? ProcessPath(int pid)
    {
        try
        {
            return File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Root and the accounts of people (from 1000 up); system accounts and ones that cannot sign in are left out.</summary>
    private static bool IsPerson(int uid, string shell)
        => uid == 0 || (uid >= 1000 && uid != 65534 && !shell.EndsWith("nologin", StringComparison.Ordinal) && !shell.EndsWith("/false", StringComparison.Ordinal));

    private sealed record Session(string UserName, string State, string Type, string? Host, DateTime? SignedInUtc, int? IdleSeconds, bool IsActive);

    /// <summary>Sessions from logind, which knows the idle and lock state; who on systems without it (containers, older systems).</summary>
    private static List<Session> ReadSessions()
    {
        var sessions = new List<Session>();
        if (Command.Run("loginctl", "list-sessions --no-legend --no-pager") is { Succeeded: true } list)
        {
            foreach (var id in Lines(list.Output).Select(Words).Where(w => w.Length >= 3).Select(w => w[0]))
            {
                if (ReadLogindSession(id) is { } session)
                {
                    sessions.Add(session);
                }
            }

            return sessions;
        }

        return UnixLogins.Who()
            .Select(entry => new Session(entry.UserName, "Active", entry.Host is null ? "Console" : "SSH", entry.Host, entry.SignedInUtc, null, true))
            .ToList();
    }

    private static Session? ReadLogindSession(string id)
    {
        var result = Command.Run("loginctl",
            $"show-session {id} --no-pager -p Name -p State -p Type -p Class -p Remote -p RemoteHost -p Service -p Timestamp -p IdleHint -p IdleSinceHint -p LockedHint");
        if (result is not { Succeeded: true })
        {
            return null;
        }

        var values = Lines(result.Output)
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        if (values.GetValueOrDefault("Name") is not { Length: > 0 } name || values.GetValueOrDefault("Class") is { } cls && cls != "user")
        {
            return null;
        }

        var locked = values.GetValueOrDefault("LockedHint") == "yes";
        var state = values.GetValueOrDefault("State") switch
        {
            "active" when locked => "Locked",
            "active" => "Active",
            "online" => "Online",
            "closing" => "Closing",
            var other => Capitalize(other ?? string.Empty)
        };
        var remote = values.GetValueOrDefault("Remote") == "yes";
        var type = remote
            ? values.GetValueOrDefault("Service") == "sshd" ? "SSH" : "Remote"
            : values.GetValueOrDefault("Type") is "x11" or "wayland" or "mir" ? "Desktop" : "Console";
        int? idle = values.GetValueOrDefault("IdleHint") == "yes"
                    && long.TryParse(values.GetValueOrDefault("IdleSinceHint"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var since) && since > 0
            ? (int)Math.Max(0, (DateTime.UtcNow - DateTime.UnixEpoch.AddTicks(since * 10)).TotalSeconds)
            : values.ContainsKey("IdleHint") ? 0 : null;
        return new Session(
            name,
            state,
            type,
            values.GetValueOrDefault("RemoteHost") is { Length: > 0 } host ? host : null,
            UnixLogins.ParseDate(values.GetValueOrDefault("Timestamp") ?? string.Empty),
            idle,
            state == "Active" && (idle is null || idle < ActiveIdleLimit.TotalSeconds));
    }

    private static InstalledProgram? ReadDesktopEntry(string file)
    {
        var entry = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var inMain = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inMain = line == "[Desktop Entry]";
                    continue;
                }

                var equals = line.IndexOf('=');
                if (inMain && equals > 0 && !line.StartsWith('#'))
                {
                    entry.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (entry.GetValueOrDefault("Type") != "Application"
            || entry.GetValueOrDefault("NoDisplay") == "true"
            || entry.GetValueOrDefault("Hidden") == "true"
            || entry.GetValueOrDefault("Name") is not { Length: > 0 } name
            || entry.GetValueOrDefault("Exec") is not { Length: > 0 } exec)
        {
            return null;
        }

        var executable = ResolveExecutable(exec);
        var folder = executable is null ? null : Path.GetDirectoryName(executable);
        return new InstalledProgram(
            name,
            null,
            entry.GetValueOrDefault("X-AppImage-Version"),
            File.GetLastWriteTimeUtc(file),
            folder is null || SharedFolders.Contains(folder) ? null : folder,
            executable,
            null);
    }

    /// <summary>The program an Exec line starts (without field codes or env), found on the path, with links followed.</summary>
    private static string? ResolveExecutable(string exec)
    {
        var words = Words(exec.Replace("\"", string.Empty)).Where(word => !word.StartsWith('%')).ToList();
        if (words.Count > 0 && Path.GetFileName(words[0]) == "env")
        {
            words = words.Skip(1).SkipWhile(word => word.Contains('=') || word.StartsWith('-')).ToList();
        }

        if (words.Count == 0)
        {
            return null;
        }

        var program = words[0];
        var path = Path.IsPathRooted(program)
            ? program
            : SearchPath.Split(':').Select(dir => Path.Combine(dir, program)).FirstOrDefault(File.Exists);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static IEnumerable<string[]> ReadColonFile(string path)
        => Lines(LinuxFiles.Read(path)).Where(line => !line.StartsWith('#')).Select(line => line.Split(':'));

    private static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Words(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
