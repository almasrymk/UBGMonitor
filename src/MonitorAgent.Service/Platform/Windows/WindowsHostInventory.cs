using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform.Windows;

/// <summary>
/// Programs from the uninstall entries in the registry (the machine's and each signed-in user's), local accounts from
/// the account database, sign-ins from Remote Desktop Services (which also covers the console), and services from WMI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostInventory : IHostInventory
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
    private const string AdministratorsSid = "S-1-5-32-544";
    private const uint ErrorServiceNeverStarted = 1077;
    private const uint ErrorServiceSpecific = 1066;

    /// <summary>Signed-in users idle for longer than this are not counted as using the device.</summary>
    private static readonly TimeSpan ActiveIdleLimit = TimeSpan.FromMinutes(5);

    public IReadOnlyList<InstalledProgram> ReadPrograms()
    {
        var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            ReadUninstallEntries(machine, found);
        }

        // HKEY_USERS holds the users who are signed in; programs installed for one user only are listed there.
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
        foreach (var sid in users.GetSubKeyNames().Where(IsUserSid))
        {
            try
            {
                using var hive = users.OpenSubKey(sid);
                if (hive is not null)
                {
                    ReadUninstallEntries(hive, found);
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }

        return found.Values.ToList();
    }

    public IReadOnlyList<UserAccountDto> ReadUsers()
    {
        var machineName = Environment.MachineName;
        var admins = AdministratorSids();
        var profiles = ReadProfiles();
        var sessions = ReadSessions();
        var accounts = new List<UserAccountDto>();

        foreach (var user in LocalAccounts())
        {
            var sid = TrySid(machineName, user.Name);
            var profile = sid is null ? null : profiles.GetValueOrDefault(sid);
            accounts.Add(Account(
                user.Name, machineName, isLocal: true, sid, profile, sessions,
                fullName: user.FullName,
                description: user.Comment,
                enabled: (user.Flags & UfAccountDisable) == 0,
                isAdmin: user.Privilege == UserPrivAdmin || (sid is not null && admins.Contains(sid)),
                lastLogonUtc: user.LastLogon > 0 ? DateTime.UnixEpoch.AddSeconds(user.LastLogon) : profile?.LastUsedUtc,
                logonCount: user.LogonCount is > 0 and < int.MaxValue ? (int)user.LogonCount : null,
                passwordLastSetUtc: user.PasswordAge > 0 ? DateTime.UtcNow.AddSeconds(-user.PasswordAge) : null));
        }

        // Domain and Microsoft Entra accounts are not in the local database; the ones that have used this device have a profile here.
        var listed = accounts.Select(a => $"{a.Domain}\\{a.UserName}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, profile) in profiles)
        {
            var (domain, name) = AccountName(sid, profile.Path);
            if (name is null || !listed.Add($"{domain}\\{name}"))
            {
                continue;
            }

            accounts.Add(Account(
                name, domain, isLocal: false, sid, profile, sessions,
                fullName: null, description: null, enabled: true,
                isAdmin: admins.Contains(sid),
                lastLogonUtc: profile.LastUsedUtc, logonCount: null, passwordLastSetUtc: null));
        }

        foreach (var session in sessions.Where(s => listed.Add($"{s.Domain}\\{s.UserName}")))
        {
            accounts.Add(Account(
                session.UserName, session.Domain, isLocal: false, null, null, sessions,
                fullName: null, description: null, enabled: true, isAdmin: false,
                lastLogonUtc: session.SignedInUtc, logonCount: null, passwordLastSetUtc: null));
        }

        return accounts;
    }

    public IReadOnlyList<SystemServiceDto> ReadServices()
    {
        var services = new List<SystemServiceDto>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, Description, State, StartMode, StartName, ProcessId, PathName, ExitCode, ServiceSpecificExitCode FROM Win32_Service");
        using var results = searcher.Get();
        using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesKey);
        foreach (ManagementObject obj in results)
        {
            using (obj)
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                using var key = servicesKey?.OpenSubKey(name);
                var delayed = key?.GetValue("DelayedAutostart") is 1;
                bool triggered;
                using (var trigger = key?.OpenSubKey("TriggerInfo"))
                {
                    triggered = trigger is not null;
                }

                var state = obj["State"]?.ToString() ?? "Unknown";
                var startMode = obj["StartMode"]?.ToString() ?? string.Empty;
                var exitCode = obj["ExitCode"] is uint code ? code : 0;
                var specificCode = obj["ServiceSpecificExitCode"] is uint specific ? specific : 0;
                var (health, problem) = ServiceHealth(state, startMode == "Auto" && !delayed && !triggered, exitCode, specificCode);
                services.Add(new SystemServiceDto
                {
                    Name = name,
                    DisplayName = obj["DisplayName"]?.ToString() ?? name,
                    Description = obj["Description"]?.ToString(),
                    State = state,
                    StartMode = StartModeText(startMode, delayed, triggered),
                    Account = obj["StartName"]?.ToString(),
                    ProcessId = obj["ProcessId"] is uint pid && pid > 0 ? (int)pid : null,
                    Path = obj["PathName"]?.ToString(),
                    Health = health,
                    Problem = problem
                });
            }
        }

        return services;
    }

    public string? ProcessPath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static (string Health, string? Problem) ServiceHealth(string state, bool startsWithWindows, uint exitCode, uint specificCode)
    {
        switch (state)
        {
            case "Running":
                return ("Healthy", null);
            case "Stopped" when exitCode != 0 && exitCode != ErrorServiceNeverStarted:
                return ("Critical", exitCode == ErrorServiceSpecific
                    ? $"Stopped with service error {specificCode}"
                    : $"Stopped with error {exitCode}: {new Win32Exception((int)exitCode).Message}");
            case "Stopped" when startsWithWindows:
                return ("Warning", "Set to start with Windows but is not running");
            case "Stopped":
                return ("Unknown", null);
            case "Paused":
                return ("Warning", "Paused");
            default:
                return ("Warning", state);
        }
    }

    private static string StartModeText(string startMode, bool delayed, bool triggered) => startMode switch
    {
        "Auto" when delayed => "Automatic (Delayed)",
        "Auto" when triggered => "Automatic (Trigger)",
        "Auto" => "Automatic",
        "Manual" when triggered => "Manual (Trigger)",
        _ => startMode
    };

    private static UserAccountDto Account(
        string name, string? domain, bool isLocal, string? sid, Profile? profile, List<Session> sessions,
        string? fullName, string? description, bool enabled, bool isAdmin,
        DateTime? lastLogonUtc, int? logonCount, DateTime? passwordLastSetUtc)
    {
        var session = sessions
            .Where(s => string.Equals(s.UserName, name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(s.Domain, domain, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.State == "Active" ? 0 : 1)
            .FirstOrDefault();
        return new UserAccountDto
        {
            UserName = name,
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            Domain = domain,
            IsLocal = isLocal,
            Enabled = enabled,
            IsAdmin = isAdmin,
            IsSignedIn = session is not null,
            IsActive = session?.IsActive == true,
            SessionState = session?.State,
            SessionType = session?.Type,
            ClientName = session?.ClientName,
            SignedInUtc = session?.SignedInUtc,
            IdleSeconds = session?.IdleSeconds,
            LastLogonUtc = lastLogonUtc,
            LogonCount = logonCount,
            PasswordLastSetUtc = passwordLastSetUtc,
            ProfilePath = profile?.Path,
            Description = string.IsNullOrWhiteSpace(description) ? null : description
        };
    }

    private static void ReadUninstallEntries(RegistryKey root, Dictionary<string, InstalledProgram> found)
    {
        using var key = root.OpenSubKey(UninstallKey);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetSubKeyNames())
        {
            try
            {
                using var entry = key.OpenSubKey(name);
                if (ReadProgram(entry) is { } program)
                {
                    found.TryAdd($"{program.Name}|{program.Version}", program);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }
    }

    private static InstalledProgram? ReadProgram(RegistryKey? key)
    {
        if (key is null
            || key.GetValue("SystemComponent") is 1
            || key.GetValue("ParentKeyName") is string { Length: > 0 }
            || key.GetValue("ReleaseType") is "Update" or "Hotfix" or "Security Update"
            || (key.GetValue("DisplayName") as string)?.Trim() is not { Length: > 0 } name)
        {
            return null;
        }

        var location = CleanPath(key.GetValue("InstallLocation") as string);
        if (location is not null && !Directory.Exists(location))
        {
            location = null;
        }

        var icon = CleanPath(key.GetValue("DisplayIcon") as string);
        return new InstalledProgram(
            name,
            Text(key.GetValue("Publisher")),
            Text(key.GetValue("DisplayVersion")),
            DateTime.TryParseExact(key.GetValue("InstallDate") as string, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            location,
            IsProgramExecutable(icon) ? icon : FindExecutable(location, name),
            key.GetValue("EstimatedSize") is int kb && kb > 0 ? Math.Round(kb / 1024d, 1) : null);
    }

    /// <summary>A path from DisplayIcon or InstallLocation, without quotes, environment variables or a trailing ",index".</summary>
    private static string? CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(value.Trim()).Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 2 && int.TryParse(text[(comma + 1)..].Trim(), out _))
        {
            text = text[..comma].Trim().Trim('"');
        }

        return text.Length == 0 ? null : text;
    }

    private static string? FindExecutable(string? folder, string displayName)
    {
        if (folder is null)
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.exe");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var candidates = files.Where(IsProgramExecutable).ToList();
        return candidates.FirstOrDefault(file => displayName.Contains(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    private static bool IsProgramExecutable(string? path)
    {
        if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return !new[] { "uninst", "unins", "setup", "update", "crash", "reporter" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Text(object? value) => value?.ToString()?.Trim() is { Length: > 0 } text ? text : null;

    /// <summary>Accounts of people (local, domain or Microsoft Entra); the system's own accounts are left out.</summary>
    private static bool IsUserSid(string sid)
        => (sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.StartsWith("S-1-12-1-", StringComparison.Ordinal))
           && !sid.EndsWith("_Classes", StringComparison.Ordinal);

    private static string? TrySid(string domain, string name)
    {
        try
        {
            return new NTAccount(domain, name).Translate(typeof(SecurityIdentifier)).Value;
        }
        catch (SystemException)
        {
            return null;
        }
    }

    /// <summary>The account of a profile; the profile folder's name when the domain cannot be reached to look it up.</summary>
    private static (string? Domain, string? Name) AccountName(string sid, string? profilePath)
    {
        try
        {
            var account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            var slash = account.IndexOf('\\');
            return slash < 0 ? (null, account) : (account[..slash], account[(slash + 1)..]);
        }
        catch (SystemException)
        {
            return (null, string.IsNullOrWhiteSpace(profilePath) ? null : Path.GetFileName(profilePath));
        }
    }

    private sealed record Profile(string? Path, DateTime? LastUsedUtc);

    private static Dictionary<string, Profile> ReadProfiles()
    {
        var profiles = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
        using var list = Registry.LocalMachine.OpenSubKey(ProfileListKey);
        if (list is null)
        {
            return profiles;
        }

        foreach (var sid in list.GetSubKeyNames().Where(IsUserSid))
        {
            using var key = list.OpenSubKey(sid);
            if (key is null)
            {
                continue;
            }

            // Windows 10 and later keep when the profile was last loaded, which is the last sign-in.
            DateTime? lastUsed = key.GetValue("LocalProfileLoadTimeHigh") is int high && key.GetValue("LocalProfileLoadTimeLow") is int low
                ? DateTime.FromFileTimeUtc(((long)(uint)high << 32) | (uint)low)
                : null;
            profiles[sid] = new Profile(key.GetValue("ProfileImagePath") as string, lastUsed);
        }

        return profiles;
    }

    private static HashSet<string> AdministratorSids()
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string group;
        try
        {
            // The group's name depends on the Windows language.
            group = new SecurityIdentifier(AdministratorsSid).Translate(typeof(NTAccount)).Value.Split('\\').Last();
        }
        catch (SystemException)
        {
            return sids;
        }

        if (NetLocalGroupGetMembers(null, group, 0, out var buffer, MaxPreferredLength, out var read, out _, IntPtr.Zero) != 0)
        {
            return sids;
        }

        try
        {
            for (var i = 0; i < read; i++)
            {
                var sid = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);
                sids.Add(new SecurityIdentifier(sid).Value);
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }

        return sids;
    }

    private static List<UserInfo2> LocalAccounts()
    {
        var users = new List<UserInfo2>();
        var resume = 0;
        if (NetUserEnum(null, 2, FilterNormalAccount, out var buffer, MaxPreferredLength, out var read, out _, ref resume) != 0)
        {
            return users;
        }

        try
        {
            var size = Marshal.SizeOf<UserInfo2>();
            for (var i = 0; i < read; i++)
            {
                users.Add(Marshal.PtrToStructure<UserInfo2>(buffer + i * size));
            }
        }
        finally
        {
            NetApiBufferFree(buffer);
        }

        return users;
    }

    private sealed record Session(
        string UserName, string? Domain, string State, string Type, string? ClientName,
        DateTime? SignedInUtc, int? IdleSeconds, bool IsActive);

    private static List<Session> ReadSessions()
    {
        var sessions = new List<Session>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
        {
            return sessions;
        }

        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(list + i * size);
                if (ReadSession(info.SessionId) is { } session)
                {
                    sessions.Add(session);
                }
            }
        }
        finally
        {
            WTSFreeMemory(list);
        }

        return sessions;
    }

    private static Session? ReadSession(int sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WtsSessionInfoEx, out var buffer, out _))
        {
            return null;
        }

        WtsInfoExLevel1 info;
        try
        {
            // WTSINFOEX is a DWORD level followed by the level-1 data, which starts on an 8-byte boundary.
            info = Marshal.PtrToStructure<WtsInfoExLevel1>(buffer + 8);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }

        if (string.IsNullOrWhiteSpace(info.UserName))
        {
            return null;
        }

        var locked = info.SessionFlags == WtsSessionStateLock;
        var state = info.SessionState switch
        {
            WtsActive when locked => "Locked",
            WtsActive => "Active",
            WtsDisconnected => "Disconnected",
            WtsIdle => "Idle",
            _ => "Connected"
        };
        int? idle = info.LastInputTime > 0 && info.CurrentTime > info.LastInputTime
            ? (int)((info.CurrentTime - info.LastInputTime) / TimeSpan.TicksPerSecond)
            : null;
        var remote = QueryUShort(sessionId, WtsClientProtocolType) == 2;
        var client = remote ? QueryString(sessionId, WtsClientName) : null;
        return new Session(
            info.UserName,
            string.IsNullOrWhiteSpace(info.DomainName) ? null : info.DomainName,
            state,
            remote ? "Remote Desktop" : sessionId == WTSGetActiveConsoleSessionId() ? "Console" : "Local",
            string.IsNullOrWhiteSpace(client) ? null : client,
            info.LogonTime > 0 ? DateTime.FromFileTimeUtc(info.LogonTime) : null,
            idle,
            state == "Active" && (idle is null || idle < ActiveIdleLimit.TotalSeconds));
    }

    private static string? QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static ushort? QueryUShort(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytes))
        {
            return null;
        }

        try
        {
            return bytes >= 2 ? (ushort)Marshal.ReadInt16(buffer) : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaxPreferredLength = -1;
    private const int FilterNormalAccount = 0x0002;
    private const int UfAccountDisable = 0x0002;
    private const int UserPrivAdmin = 2;
    private const int WtsClientName = 10;
    private const int WtsClientProtocolType = 16;
    private const int WtsSessionInfoEx = 25;
    private const int WtsActive = 0;
    private const int WtsDisconnected = 4;
    private const int WtsIdle = 5;
    private const int WtsSessionStateLock = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo2
    {
        public string Name;
        public string? Password;
        public uint PasswordAge;
        public uint Privilege;
        public string? HomeDir;
        public string? Comment;
        public uint Flags;
        public string? ScriptPath;
        public uint AuthFlags;
        public string? FullName;
        public string? UserComment;
        public string? Parameters;
        public string? Workstations;
        public uint LastLogon;
        public uint LastLogoff;
        public uint AccountExpires;
        public uint MaxStorage;
        public uint UnitsPerWeek;
        public IntPtr LogonHours;
        public uint BadPasswordCount;
        public uint LogonCount;
        public string? LogonServer;
        public uint CountryCode;
        public uint CodePage;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WtsInfoExLevel1
    {
        public int SessionId;
        public int SessionState;
        public int SessionFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 18)] public string DomainName;
        public long LogonTime;
        public long ConnectTime;
        public long DisconnectTime;
        public long LastInputTime;
        public long CurrentTime;
        public uint IncomingBytes;
        public uint OutgoingBytes;
        public uint IncomingFrames;
        public uint OutgoingFrames;
        public uint IncomingCompressedBytes;
        public uint OutgoingCompressedBytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, char[] exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(string? server, int level, int filter, out IntPtr buffer, int maxLength,
        out int entriesRead, out int totalEntries, ref int resumeHandle);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? server, string group, int level, out IntPtr buffer, int maxLength,
        out int entriesRead, out int totalEntries, IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
