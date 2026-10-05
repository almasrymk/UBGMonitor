using System.Diagnostics;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.SystemInfo;

/// <summary>The Applications screen: installed programs with what they use now, user accounts, and system services.</summary>
public interface IApplicationsService
{
    Task<IReadOnlyList<InstalledProgramDto>> GetProgramsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccountDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>The installed programs alone, without measuring what their processes use.</summary>
    Task<IReadOnlyList<InstalledProgram>> GetInstalledProgramsAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationsService : IApplicationsService
{
    private readonly IHostInventory _inventory;
    private readonly ProcessUsageSampler _usage;
    private readonly ILogger<ApplicationsService> _logger;
    private readonly Cached<IReadOnlyList<InstalledProgram>> _programs;
    private readonly Cached<IReadOnlyList<UserAccountDto>> _users;
    private readonly Cached<IReadOnlyList<SystemServiceDto>> _services;

    public ApplicationsService(IHostInventory inventory, ProcessUsageSampler usage, ILocalConfigCache config, ILogger<ApplicationsService> logger)
    {
        _inventory = inventory;
        _usage = usage;
        _logger = logger;
        _programs = new(() => Lifetime(config.GetGeneral().ProgramsIntervalSeconds));
        _users = new(() => Lifetime(config.GetGeneral().UsersIntervalSeconds));
        _services = new(() => Lifetime(config.GetGeneral().ServicesIntervalSeconds));
    }

    public Task<IReadOnlyList<InstalledProgramDto>> GetProgramsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var programs = _programs.Get(() => Load("programs", _inventory.ReadPrograms));
            return ProgramUsage.Build(programs, _usage.Read(), OperatingSystem.IsWindows());
        }, cancellationToken);

    public Task<IReadOnlyList<InstalledProgram>> GetInstalledProgramsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => _programs.Get(() => Load("programs", _inventory.ReadPrograms)), cancellationToken);

    public Task<IReadOnlyList<UserAccountDto>> GetUsersAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => _users.Get(() => Load("users", _inventory.ReadUsers)), cancellationToken);

    public Task<IReadOnlyList<SystemServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => _services.Get(() => Load("services", _inventory.ReadServices)), cancellationToken);

    private IReadOnlyList<T> Load<T>(string what, Func<IReadOnlyList<T>> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the {What} of this device", what);
            return [];
        }
    }

    /// <summary>A little under the interval, so a reader asking exactly every interval always gets a fresh list.</summary>
    private static TimeSpan Lifetime(int seconds) => TimeSpan.FromSeconds(Math.Max(1, seconds)) - TimeSpan.FromMilliseconds(250);

    private sealed class Cached<T>(Func<TimeSpan> lifetime) where T : class
    {
        private readonly object _lock = new();
        private T? _value;
        private long _loadedAt;

        public T Get(Func<T> load)
        {
            lock (_lock)
            {
                if (_value is null || Stopwatch.GetElapsedTime(_loadedAt) > lifetime())
                {
                    _value = load();
                    _loadedAt = Stopwatch.GetTimestamp();
                }

                return _value;
            }
        }
    }
}

/// <summary>Adds up the processes of each installed program: the ones running from its folder, or from its executable.</summary>
public static class ProgramUsage
{
    /// <summary>Folders shared by many programs; a program "installed" there has no folder of its own to match processes by.</summary>
    private static readonly HashSet<string> SharedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files", "Program Files (x86)", "ProgramData", "Users", "AppData", "Local", "LocalLow", "Roaming", "Programs",
        "Common Files", "Microsoft", "WindowsApps", "Windows", "System32", "SysWOW64", "bin", "sbin", "lib", "lib64", "libexec",
        "share", "opt", "usr", "local", "Applications", "Utilities"
    };

    public static IReadOnlyList<InstalledProgramDto> Build(IReadOnlyList<InstalledProgram> programs, IReadOnlyList<ProcessUsage> processes, bool windows)
    {
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var folders = programs
            .Select((program, index) => (Index: index, Folder: OwnFolder(program.InstallLocation, windows)))
            .Where(entry => entry.Folder is not null)
            .OrderByDescending(entry => entry.Folder!.Length)
            .ToList();
        var executables = new Dictionary<string, int>(comparer);
        var names = new Dictionary<string, int?>(comparer);
        for (var i = 0; i < programs.Count; i++)
        {
            if (programs[i].ExecutablePath is { Length: > 0 } exe)
            {
                executables.TryAdd(exe, i);
                var name = Path.GetFileName(exe);
                names[name] = names.ContainsKey(name) ? null : i;
            }
        }

        var totals = new Totals[programs.Count];
        foreach (var process in processes)
        {
            int? owner = null;
            if (process.Path is { Length: > 0 } path)
            {
                owner = folders.FirstOrDefault(entry => path.StartsWith(entry.Folder!, comparison)) is { Folder: not null } match
                    ? match.Index
                    : executables.TryGetValue(path, out var byExe) ? byExe : null;
            }

            // Without the path (or for a launcher script on Linux), the process name is used when exactly one program has it.
            if (owner is null && !windows && names.TryGetValue(process.Name, out var byName))
            {
                owner = byName;
            }

            if (owner is { } index)
            {
                totals[index].Add(process);
            }
        }

        return programs
            .Select((program, i) => new InstalledProgramDto
            {
                Name = program.Name,
                Publisher = program.Publisher,
                Version = program.Version,
                InstallDate = program.InstallDate,
                InstallLocation = program.InstallLocation,
                ExecutablePath = program.ExecutablePath,
                SizeMB = program.SizeMB,
                IsRunning = totals[i].Count > 0,
                ProcessCount = totals[i].Count,
                CpuPercent = Math.Round(totals[i].Cpu, 1),
                RamMB = Math.Round(totals[i].Ram, 1),
                NetworkKBps = Math.Round(totals[i].Network, 1),
                DiskKBps = Math.Round(totals[i].Disk, 1)
            })
            .OrderByDescending(p => p.IsRunning)
            .ThenByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.RamMB)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The program's folder with a trailing separator, or null when it is a folder many programs share.</summary>
    public static string? OwnFolder(string? location, bool windows)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var folder = Path.TrimEndingDirectorySeparator(location.Trim());
        var parts = folder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var depth = windows ? parts.Length - 1 : parts.Length;
        if (depth < 2 || SharedFolderNames.Contains(parts[^1]))
        {
            return null;
        }

        if (windows && Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } system
            && (folder + "\\").StartsWith(system + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return folder + (windows ? "\\" : "/");
    }

    private struct Totals
    {
        public int Count;
        public double Cpu;
        public double Ram;
        public double Network;
        public double Disk;

        public void Add(ProcessUsage process)
        {
            Count++;
            Cpu += process.CpuPercent;
            Ram += process.RamMB;
            Network += process.NetworkKBps;
            Disk += process.DiskKBps;
        }
    }
}
