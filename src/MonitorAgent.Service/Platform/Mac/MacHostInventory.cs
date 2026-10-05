using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Mac;

/// <summary>Programs from the .app bundles, accounts from the directory service, sign-ins from who and last, and services from launchd.</summary>
public sealed partial class MacHostInventory : BasicHostInventory
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    public override IReadOnlyList<InstalledProgram> ReadPrograms()
    {
        var roots = new List<string> { "/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities" };
        roots.AddRange(Directories("/Users").Select(home => Path.Combine(home, "Applications")));

        var found = new Dictionary<string, InstalledProgram>(StringComparer.Ordinal);
        foreach (var bundle in roots.SelectMany(root => Directories(root, "*.app")))
        {
            var info = ReadPlist(Path.Combine(bundle, "Contents", "Info.plist"));
            var name = info.GetValueOrDefault("CFBundleDisplayName") ?? info.GetValueOrDefault("CFBundleName") ?? Path.GetFileNameWithoutExtension(bundle);
            var executable = Path.Combine(bundle, "Contents", "MacOS", info.GetValueOrDefault("CFBundleExecutable") ?? Path.GetFileNameWithoutExtension(bundle));
            found.TryAdd(bundle, new InstalledProgram(
                name,
                null,
                info.GetValueOrDefault("CFBundleShortVersionString") ?? info.GetValueOrDefault("CFBundleVersion"),
                Directory.GetLastWriteTimeUtc(bundle),
                bundle,
                File.Exists(executable) ? executable : null,
                null));
        }

        return found.Values.ToList();
    }

    public override IReadOnlyList<UserAccountDto> ReadUsers()
    {
        var admins = Command.Run("dscl", ". -read /Groups/admin GroupMembership") is { Succeeded: true } group
            ? group.Output.Replace("GroupMembership:", string.Empty).Split(' ', '\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : [];
        var sessions = UnixLogins.Who();
        var logons = UnixLogins.Last(string.Empty);
        var users = new List<UserAccountDto>();
        if (Command.Run("dscl", ". -list /Users UniqueID") is not { Succeeded: true } list)
        {
            return users;
        }

        foreach (var words in list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            // People's accounts start at 501; the ones below and the ones starting with "_" belong to the system.
            if (words.Length < 2 || words[0].StartsWith('_') || !int.TryParse(words[1], out var uid) || uid < 501)
            {
                continue;
            }

            var name = words[0];
            var details = ReadUser(name);
            var session = sessions.Where(s => s.UserName == name).OrderBy(s => s.Line == "console" ? 0 : 1).FirstOrDefault();
            var logon = logons.GetValueOrDefault(name);
            users.Add(new UserAccountDto
            {
                UserName = name,
                FullName = details.GetValueOrDefault("RealName"),
                Domain = Environment.MachineName,
                IsLocal = true,
                Enabled = details.GetValueOrDefault("AuthenticationAuthority")?.Contains("DisabledUser", StringComparison.Ordinal) != true,
                IsAdmin = admins.Contains(name),
                IsSignedIn = session is not null,
                IsActive = session?.Line == "console",
                SessionState = session is null ? null : "Active",
                SessionType = session is null ? null : session.Line == "console" ? "Console" : session.Host is null ? "Terminal" : "SSH",
                ClientName = session?.Host,
                SignedInUtc = session?.SignedInUtc,
                LastLogonUtc = logon?.LatestUtc ?? session?.SignedInUtc,
                LogonCount = logon?.Count,
                ProfilePath = details.GetValueOrDefault("NFSHomeDirectory")
            });
        }

        return users;
    }

    public override IReadOnlyList<SystemServiceDto> ReadServices()
    {
        var services = new List<SystemServiceDto>();
        if (Command.Run("launchctl", "list") is not { Succeeded: true } list)
        {
            return services;
        }

        foreach (var words in list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split('\t')))
        {
            if (words.Length < 3)
            {
                continue;
            }

            var pid = int.TryParse(words[0], out var parsedPid) ? parsedPid : (int?)null;
            var status = int.TryParse(words[1], out var parsedStatus) ? parsedStatus : 0;
            var label = words[2].Trim();
            var (state, health, problem) = pid is not null
                ? ("Running", "Healthy", (string?)null)
                : status != 0
                    ? ("Failed", "Critical", status < 0 ? $"Ended by signal {-status}" : $"Exited with code {status}")
                    : ("Stopped", "Unknown", null);
            services.Add(new SystemServiceDto
            {
                Name = label,
                DisplayName = label,
                State = state,
                StartMode = "launchd",
                ProcessId = pid,
                Health = health,
                Problem = problem
            });
        }

        return services;
    }

    public override string? ProcessPath(int pid)
    {
        var buffer = new byte[4096];
        var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
    }

    /// <summary>The single-line attributes of a user record ("RealName: Name", or the name on the next line).</summary>
    private static Dictionary<string, string> ReadUser(string name)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Command.Run("dscl", $". -read /Users/{name} RealName NFSHomeDirectory AuthenticationAuthority") is not { Succeeded: true } result)
        {
            return values;
        }

        string? pending = null;
        foreach (var line in result.Output.Split('\n'))
        {
            if (pending is not null && line.StartsWith(' '))
            {
                values.TryAdd(pending, line.Trim());
                pending = null;
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0 || line.StartsWith(' '))
            {
                continue;
            }

            var key = line[..colon];
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                pending = key;
            }
            else
            {
                values.TryAdd(key, value);
            }
        }

        return values;
    }

    /// <summary>The string values of an Info.plist; binary ones are converted with plutil first.</summary>
    private static Dictionary<string, string> ReadPlist(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return values;
        }

        if (!text.StartsWith("<?xml", StringComparison.Ordinal))
        {
            text = Command.Run("plutil", $"-convert xml1 -o - \"{path}\"") is { Succeeded: true } converted ? converted.Output : string.Empty;
        }

        foreach (Match match in PlistString().Matches(text))
        {
            values.TryAdd(match.Groups[1].Value, System.Net.WebUtility.HtmlDecode(match.Groups[2].Value));
        }

        return values;
    }

    private static IEnumerable<string> Directories(string root, string pattern = "*")
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"<key>([^<]+)</key>\s*<string>([^<]*)</string>")]
    private static partial Regex PlistString();

    [DllImport(LibSystem)]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint bufferSize);
}
