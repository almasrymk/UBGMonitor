using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

/// <summary>The Applications tab: the programs installed on the device, its users, and its services, as the service reads them.</summary>
public sealed partial class ApplicationsViewModel : ObservableObject
{
    public const string ProgramsSection = "Programs";
    public const string UsersSection = "Users";
    public const string ServicesSection = "Services";

    private readonly AgentApiClient _client;
    private IReadOnlyList<InstalledProgramDto> _programs = [];
    private IReadOnlyList<UserAccountDto> _users = [];
    private IReadOnlyList<SystemServiceDto> _services = [];
    private int _loading;
    private int _generation;

    [ObservableProperty] private string _selectedSection = ProgramsSection;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _onlyRunning;
    [ObservableProperty] private bool _onlySignedIn;
    [ObservableProperty] private bool _onlyProblems;
    [ObservableProperty] private string _programsSummary = "-";
    [ObservableProperty] private string _usersSummary = "-";
    [ObservableProperty] private string _servicesSummary = "-";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ApplicationsViewModel(AgentApiClient client)
    {
        _client = client;
    }

    public ObservableCollection<ProgramRowViewModel> Programs { get; } = [];

    public ObservableCollection<UserRowViewModel> Users { get; } = [];

    public ObservableCollection<ServiceRowViewModel> Services { get; } = [];

    public bool IsPrograms => SelectedSection == ProgramsSection;

    public bool IsUsers => SelectedSection == UsersSection;

    public bool IsServices => SelectedSection == ServicesSection;

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsPrograms));
        OnPropertyChanged(nameof(IsUsers));
        OnPropertyChanged(nameof(IsServices));
        RefreshCommand.Execute(null);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnOnlyRunningChanged(bool value) => ApplyFilters();

    partial void OnOnlySignedInChanged(bool value) => ApplyFilters();

    partial void OnOnlyProblemsChanged(bool value) => ApplyFilters();

    /// <summary>Asks the service for the section on screen; a request still on its way is not repeated.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1)
        {
            return;
        }

        var generation = _generation;
        try
        {
            switch (SelectedSection)
            {
                case ProgramsSection:
                    var programs = await _client.GetProgramsAsync();
                    if (programs is not null)
                    {
                        // Reading icons from the executables takes a moment the first time, so it is done away from the screen.
                        await Task.Run(() => programs.ForEach(p => ProgramRowViewModel.IconFor(p.ExecutablePath)));
                    }

                    Apply(generation, programs, list => _programs = list);
                    break;
                case UsersSection:
                    Apply(generation, await _client.GetUsersAsync(), list => _users = list);
                    break;
                default:
                    Apply(generation, await _client.GetServicesAsync(), list => _services = list);
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _loading, 0);
        }
    }

    /// <summary>Empties the tab when the service stops answering or the license is missing; what it showed came from the service.</summary>
    public void Unload()
    {
        _generation++;
        _programs = [];
        _users = [];
        _services = [];
        ApplyFilters();
        ProgramsSummary = UsersSummary = ServicesSummary = "-";
        StatusMessage = string.Empty;
    }

    private void Apply<T>(int generation, List<T>? items, Action<IReadOnlyList<T>> store)
    {
        if (generation != _generation)
        {
            return;
        }

        if (items is null)
        {
            StatusMessage = "The Agent service did not answer; the list shows what it sent last.";
            return;
        }

        StatusMessage = string.Empty;
        store(items);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = SearchText.Trim();

        Sync(Programs,
            _programs.Where(p => (!OnlyRunning || p.IsRunning) && Matches(search, p.Name, p.Publisher, p.Version, p.InstallLocation))
                .OrderByDescending(p => p.IsRunning).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ProgramRowViewModel.KeyOf, p => new ProgramRowViewModel(p), (row, p) => row.Update(p));
        Sync(Users,
            _users.Where(u => (!OnlySignedIn || u.IsSignedIn) && Matches(search, u.UserName, u.FullName, u.Domain, u.ClientName))
                .OrderByDescending(u => u.IsActive).ThenByDescending(u => u.IsSignedIn).ThenByDescending(u => u.Enabled)
                .ThenBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).ToList(),
            UserRowViewModel.KeyOf, u => new UserRowViewModel(u), (row, u) => row.Update(u));
        Sync(Services,
            _services.Where(s => (!OnlyProblems || s.Problem is not null) && Matches(search, s.Name, s.DisplayName, s.Description))
                .OrderBy(s => ServiceRank(s.Health)).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            s => s.Name, s => new ServiceRowViewModel(s), (row, s) => row.Update(s));

        UpdateSummaries();
    }

    private void UpdateSummaries()
    {
        if (_programs.Count > 0)
        {
            ProgramsSummary = $"{_programs.Count} installed · {_programs.Count(p => p.IsRunning)} running";
        }

        if (_users.Count > 0)
        {
            var active = _users.Where(u => u.IsActive).Select(u => u.UserName).ToList();
            UsersSummary = $"{_users.Count} accounts · {_users.Count(u => u.IsSignedIn)} signed in"
                           + (active.Count > 0 ? $" · active: {string.Join(", ", active)}" : string.Empty);
        }

        if (_services.Count > 0)
        {
            var problems = _services.Count(s => s.Problem is not null);
            ServicesSummary = $"{_services.Count} services · {_services.Count(s => s.State == "Running")} running"
                              + (problems > 0 ? $" · {problems} with problems" : " · no problems");
        }
    }

    private static int ServiceRank(string health) => health switch
    {
        "Critical" => 0,
        "Warning" => 1,
        "Healthy" => 2,
        _ => 3
    };

    private static bool Matches(string search, params string?[] fields)
        => search.Length == 0 || fields.Any(field => field?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Brings the rows in line with the list, keeping the row objects so the grid's selection and scroll position stay.</summary>
    private static void Sync<TRow, TItem>(ObservableCollection<TRow> rows, IReadOnlyList<TItem> items,
        Func<TItem, string> keyOf, Func<TItem, TRow> create, Action<TRow, TItem> update)
        where TRow : IKeyedRow
    {
        var wanted = new HashSet<string>(items.Select(keyOf), StringComparer.OrdinalIgnoreCase);
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(rows[i].Key))
            {
                rows.RemoveAt(i);
            }
        }

        var index = new Dictionary<string, TRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            index.TryAdd(row.Key, row);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!index.TryGetValue(keyOf(item), out var row))
            {
                row = create(item);
                index[row.Key] = row;
                rows.Insert(Math.Min(i, rows.Count), row);
                continue;
            }

            update(row, item);
            var at = rows.IndexOf(row);
            if (at != i && i < rows.Count)
            {
                rows.Move(at, i);
            }
        }
    }
}

public interface IKeyedRow
{
    string Key { get; }
}

internal static class RowText
{
    public static string Time(DateTime? utc)
        => utc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";

    public static string Date(DateTime? date)
        => date is { } value ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-";

    public static string Rate(double kbps)
        => kbps <= 0 ? "0 KB/s" : kbps >= 1024 ? $"{kbps / 1024:0.0} MB/s" : $"{kbps:0.0} KB/s";

    public static string Memory(double mb)
        => mb <= 0 ? "-" : mb >= 1024 ? $"{mb / 1024:0.00} GB" : $"{mb:0.0} MB";

    public static string Duration(int? seconds) => seconds switch
    {
        null => "-",
        < 60 => "< 1 min",
        < 3600 => $"{seconds / 60} min",
        < 86400 => $"{seconds / 3600}h {seconds % 3600 / 60}m",
        _ => $"{seconds / 86400}d {seconds % 86400 / 3600}h"
    };
}

public sealed partial class ProgramRowViewModel : ObservableObject, IKeyedRow
{
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _health = "Unknown";
    [ObservableProperty] private int _processCount;
    [ObservableProperty] private double _cpu;
    [ObservableProperty] private string _cpuText = "-";
    [ObservableProperty] private double _ram;
    [ObservableProperty] private string _ramText = "-";
    [ObservableProperty] private double _network;
    [ObservableProperty] private string _networkText = "-";
    [ObservableProperty] private double _disk;
    [ObservableProperty] private string _diskText = "-";
    [ObservableProperty] private object? _icon;

    public ProgramRowViewModel(InstalledProgramDto program)
    {
        Key = KeyOf(program);
        Name = program.Name;
        Publisher = program.Publisher ?? "-";
        Version = program.Version ?? "-";
        Installed = RowText.Date(program.InstallDate);
        InstallDate = program.InstallDate;
        Size = program.SizeMB is { } mb ? RowText.Memory(mb) : "-";
        SizeMB = program.SizeMB ?? 0;
        Location = program.InstallLocation ?? program.ExecutablePath ?? "-";
        Update(program);
    }

    public string Key { get; }

    public string Name { get; }

    public string Publisher { get; }

    public string Version { get; }

    public string Installed { get; }

    public DateTime? InstallDate { get; }

    public string Size { get; }

    public double SizeMB { get; }

    public string Location { get; }

    public static string KeyOf(InstalledProgramDto program) => $"{program.Name}|{program.Version}";

    /// <summary>The program's icon: from the executable on Windows, from the launcher entry or bundle elsewhere.</summary>
    public static object? IconFor(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return ProcessIconCache.ForPath(executable);
        }

        if (InstalledProgramCatalog.Apps.Count == 0)
        {
            InstalledProgramCatalog.Load();
        }

        return SiteLogoCache.ToImage(InstalledProgramCatalog.Find(executable)?.IconBase64);
    }

    public void Update(InstalledProgramDto program)
    {
        IsRunning = program.IsRunning;
        Status = program.IsRunning ? program.ProcessCount > 1 ? $"Running ({program.ProcessCount})" : "Running" : "Not running";
        Health = program.IsRunning ? "Healthy" : "Unknown";
        ProcessCount = program.ProcessCount;
        Cpu = program.CpuPercent;
        CpuText = program.IsRunning ? $"{program.CpuPercent:0.0} %" : "-";
        Ram = program.RamMB;
        RamText = program.IsRunning ? RowText.Memory(program.RamMB) : "-";
        Network = program.NetworkKBps;
        NetworkText = program.IsRunning ? RowText.Rate(program.NetworkKBps) : "-";
        Disk = program.DiskKBps;
        DiskText = program.IsRunning ? RowText.Rate(program.DiskKBps) : "-";
        Icon ??= IconFor(program.ExecutablePath);
    }
}

public sealed partial class UserRowViewModel : ObservableObject, IKeyedRow
{
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _health = "Unknown";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string _session = "-";
    [ObservableProperty] private string _from = "-";
    [ObservableProperty] private string _signedIn = "-";
    [ObservableProperty] private string _idle = "-";
    [ObservableProperty] private int _idleSeconds;
    [ObservableProperty] private string _lastLogon = "-";
    [ObservableProperty] private DateTime? _lastLogonUtc;
    [ObservableProperty] private string _logons = "-";

    public UserRowViewModel(UserAccountDto user)
    {
        Key = KeyOf(user);
        UserName = user.UserName;
        FullName = user.FullName ?? "-";
        Account = user.IsLocal ? "Local" : string.Equals(user.Domain, "AzureAD", StringComparison.OrdinalIgnoreCase) ? "Microsoft Entra" : $"Domain ({user.Domain})";
        Role = user.IsAdmin ? "Administrator" : "Standard";
        IsAdmin = user.IsAdmin;
        Enabled = user.Enabled;
        PasswordSet = RowText.Time(user.PasswordLastSetUtc);
        Profile = user.ProfilePath ?? "-";
        Description = user.Description ?? string.Empty;
        Update(user);
    }

    public string Key { get; }

    public string UserName { get; }

    public string FullName { get; }

    public string Account { get; }

    public string Role { get; }

    public bool IsAdmin { get; }

    public bool Enabled { get; }

    public string PasswordSet { get; }

    public string Profile { get; }

    public string Description { get; }

    public static string KeyOf(UserAccountDto user) => $"{user.Domain}\\{user.UserName}";

    public void Update(UserAccountDto user)
    {
        IsActive = user.IsActive;
        IsSignedIn = user.IsSignedIn;
        (Status, Health) = user switch
        {
            { Enabled: false } => ("Disabled", "Unknown"),
            { IsActive: true } => ("Active now", "Healthy"),
            { IsSignedIn: true } => ($"Signed in ({user.SessionState ?? "idle"})", "Warning"),
            _ => ("Signed out", "Unknown")
        };
        Session = user.SessionType ?? "-";
        From = user.ClientName ?? "-";
        SignedIn = RowText.Time(user.SignedInUtc);
        Idle = user.IsSignedIn ? RowText.Duration(user.IdleSeconds) : "-";
        IdleSeconds = user.IdleSeconds ?? int.MaxValue;
        LastLogon = RowText.Time(user.LastLogonUtc);
        LastLogonUtc = user.LastLogonUtc;
        Logons = user.LogonCount?.ToString(CultureInfo.InvariantCulture) ?? "-";
    }
}

public sealed partial class ServiceRowViewModel : ObservableObject, IKeyedRow
{
    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private string _health = "Unknown";
    [ObservableProperty] private string _problem = string.Empty;
    [ObservableProperty] private string _startMode = string.Empty;
    [ObservableProperty] private string _processId = "-";

    public ServiceRowViewModel(SystemServiceDto service)
    {
        Key = service.Name;
        Name = service.Name;
        DisplayName = service.DisplayName;
        Description = service.Description ?? string.Empty;
        Account = service.Account ?? "-";
        Path = service.Path ?? "-";
        Update(service);
    }

    public string Key { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Account { get; }

    public string Path { get; }

    public void Update(SystemServiceDto service)
    {
        State = service.State;
        Health = service.Health;
        Problem = service.Problem ?? string.Empty;
        StartMode = service.StartMode;
        ProcessId = service.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "-";
    }
}
