using MonitorAgent.Service.Platform;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Tests;

public sealed class ApplicationsTests
{
    private static InstalledProgram Program(string name, string? location = null, string? exe = null)
        => new(name, null, null, null, location, exe, null);

    [Fact]
    public void Processes_Count_Toward_The_Program_Whose_Folder_They_Run_From()
    {
        var programs = new[]
        {
            Program("Code", @"C:\Program Files\Microsoft VS Code"),
            Program("Chrome", @"C:\Program Files\Google\Chrome\Application", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            Program("Notepad++", null, @"C:\Tools\Notepad++\notepad++.exe")
        };
        var processes = new[]
        {
            new ProcessUsage(1, "Code", @"C:\Program Files\Microsoft VS Code\Code.exe", 10, 300, 1, 2),
            new ProcessUsage(2, "Code", @"c:\program files\microsoft vs code\Code.exe", 5, 200, 0, 0),
            new ProcessUsage(3, "chrome", @"C:\Program Files\Google\Chrome\Application\130.0\chrome_crashpad.exe", 1, 50, 4, 0),
            new ProcessUsage(4, "notepad++", @"C:\Tools\Notepad++\notepad++.exe", 0.5, 20, 0, 1),
            new ProcessUsage(5, "svchost", @"C:\Windows\System32\svchost.exe", 3, 40, 0, 0)
        };

        var result = ProgramUsage.Build(programs, processes, windows: true).ToDictionary(p => p.Name);

        Assert.Equal(2, result["Code"].ProcessCount);
        Assert.Equal(15, result["Code"].CpuPercent);
        Assert.Equal(500, result["Code"].RamMB);
        Assert.True(result["Chrome"].IsRunning);
        Assert.Equal(4, result["Chrome"].NetworkKBps);
        Assert.Equal(1, result["Notepad++"].ProcessCount);
    }

    [Fact]
    public void Running_Programs_Come_First()
    {
        var programs = new[] { Program("Idle", @"C:\Apps\Idle"), Program("Busy", @"C:\Apps\Busy") };
        var processes = new[] { new ProcessUsage(1, "busy", @"C:\Apps\Busy\busy.exe", 1, 1, 0, 0) };

        var result = ProgramUsage.Build(programs, processes, windows: true);

        Assert.Equal(["Busy", "Idle"], result.Select(p => p.Name));
        Assert.False(result[1].IsRunning);
    }

    [WindowsTheory]
    [InlineData(@"C:\Program Files", null)]
    [InlineData(@"C:\Program Files\", null)]
    [InlineData(@"C:\Users\me\AppData\Local", null)]
    [InlineData(@"C:\Windows\System32\drivers", null)]
    [InlineData(@"C:\", null)]
    [InlineData(@"C:\Program Files\Microsoft VS Code\", @"C:\Program Files\Microsoft VS Code\")]
    [InlineData(@"D:\Apps\Tool", @"D:\Apps\Tool\")]
    public void Shared_Folders_Do_Not_Claim_Processes(string location, string? expected)
        => Assert.Equal(expected, ProgramUsage.OwnFolder(location, windows: true));

    [Fact]
    public void On_Linux_A_Launcher_Script_Matches_By_Process_Name()
    {
        var programs = new[] { Program("Visual Studio Code", null, "/usr/share/code/bin/code") };
        var processes = new[] { new ProcessUsage(1, "code", "/usr/share/code/code", 2, 100, 0, 0) };

        var result = Assert.Single(ProgramUsage.Build(programs, processes, windows: false));

        Assert.True(result.IsRunning);
    }

    [Fact]
    public void Dates_From_Who_And_Last_Are_Read()
    {
        var iso = UnixLogins.ParseDate("mk       pts/0        2026-10-05 12:30 (192.168.1.5)");
        var full = UnixLogins.ParseDate("mk       pts/0        192.168.1.5      Mon Oct  5 12:30:15 2026 - Mon Oct  5 13:00:00 2026  (00:29)");

        Assert.Equal(new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Local).ToUniversalTime(), iso);
        Assert.Equal(new DateTime(2026, 10, 5, 12, 30, 15, DateTimeKind.Local).ToUniversalTime(), full);
        Assert.Null(UnixLogins.ParseDate("wtmp begins"));
    }

    private static InstalledProgram Versioned(string name, string version) => new(name, null, version, null, null, null, null);

    private static SystemServiceDto Service(string name, string state, string startMode = "Manual", string health = "Unknown", string? problem = null)
        => new() { Name = name, DisplayName = name, State = state, StartMode = startMode, Health = health, Problem = problem };

    [Fact]
    public void Programs_Installed_Uninstalled_And_Updated_Are_Changes()
    {
        var before = new[] { Versioned("Chrome", "130.0"), Versioned("Old Tool", "1.0"), Versioned("Zip", "23.01"), Versioned("Zip", "23.01") };
        var after = new[] { Versioned("Chrome", "131.0"), Versioned("Zip", "23.01"), Versioned("New App", "2.0") };

        var changes = ApplicationChanges.ComparePrograms(before, after).Select(c => c.Text).ToList();

        Assert.Equal(["Program installed: New App 2.0", "Program uninstalled: Old Tool", "Program updated: Chrome 130.0 -> 131.0"], changes);
    }

    [Fact]
    public void Users_Signing_In_And_Out_And_Becoming_Admin_Are_Changes()
    {
        var before = new[]
        {
            new UserAccountDto { UserName = "mk", IsLocal = true },
            new UserAccountDto { UserName = "ali", IsLocal = true, IsSignedIn = true, SessionState = "Active" }
        };
        var after = new[]
        {
            new UserAccountDto { UserName = "mk", IsLocal = true, IsAdmin = true, IsSignedIn = true, SessionState = "Active", SessionType = "Remote Desktop", ClientName = "PC1" },
            new UserAccountDto { UserName = "ali", IsLocal = true, IsSignedIn = true, SessionState = "Disconnected" },
            new UserAccountDto { UserName = "guest2", IsLocal = true }
        };

        var changes = ApplicationChanges.CompareUsers(before, after);

        Assert.Contains(changes, c => c is { Text: "User account added: guest2", Severity: "Warning" });
        Assert.Contains(changes, c => c is { Text: "mk became an administrator", Severity: "Warning" });
        Assert.Contains(changes, c => c.Text == "mk signed in (Remote Desktop from PC1)");
        Assert.Contains(changes, c => c.Text == "ali disconnected (still signed in)");
        Assert.Equal(4, changes.Count);
    }

    [Fact]
    public void Services_Report_Failures_And_Recoveries_But_Not_On_Demand_Starts()
    {
        var before = new[]
        {
            Service("Spooler", "Running", "Automatic", "Healthy"),
            Service("BITS", "Running", "Manual (Trigger)", "Healthy"),
            Service("Sql", "Stopped", "Automatic", "Warning", "Set to start with Windows but is not running"),
            Service("Agent", "Running", "Automatic", "Healthy"),
            Service("Gone", "Running", "Manual", "Healthy")
        };
        var after = new[]
        {
            Service("Spooler", "Stopped", "Automatic", "Critical", "Stopped with error 1067"),
            Service("BITS", "Stopped", "Manual (Trigger)"),
            Service("Sql", "Running", "Automatic", "Healthy"),
            Service("Agent", "Stop Pending", "Automatic", "Warning", "Stop Pending"),
            Service("New", "Running", "Automatic", "Healthy")
        };

        var (changes, settled) = ApplicationChanges.CompareServices(before, after);

        Assert.Contains(changes, c => c is { Text: "Service stopped: Spooler - Stopped with error 1067", Severity: "Critical", ServiceName: "Spooler", Recovered: false });
        Assert.Contains(changes, c => c is { Text: "Service recovered: Sql - now running", ServiceName: "Sql", Recovered: true });
        Assert.Contains(changes, c => c.Text == "Service installed: New (Running, Automatic)");
        Assert.Contains(changes, c => c.Text == "Service removed: Gone");
        Assert.Equal(4, changes.Count);
        Assert.Equal("Running", settled.Single(s => s.Name == "Agent").State);
    }

    [Fact]
    public void An_Enabled_Linux_Unit_That_Stops_Is_A_Warning()
    {
        var (changes, _) = ApplicationChanges.CompareServices(
            [Service("nginx", "Running", "Enabled", "Healthy")],
            [Service("nginx", "Stopped", "Enabled")]);

        var change = Assert.Single(changes);
        Assert.Equal("Warning", change.Severity);
        Assert.Equal("Service stopped: nginx - Set to start with the system but is not running", change.Text);
    }
}
