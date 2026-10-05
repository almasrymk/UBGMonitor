namespace MonitorAgent.Shared.Models;

/// <summary>A program installed on the device, with what its running processes use right now.</summary>
public sealed class InstalledProgramDto
{
    public string Name { get; init; } = string.Empty;

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public DateTime? InstallDate { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>The program's main executable, used for its icon; empty when it could not be found.</summary>
    public string? ExecutablePath { get; init; }

    public double? SizeMB { get; init; }

    public bool IsRunning { get; init; }

    public int ProcessCount { get; init; }

    public double CpuPercent { get; init; }

    public double RamMB { get; init; }

    /// <summary>Sent plus received; zero where the system does not count network traffic per process.</summary>
    public double NetworkKBps { get; init; }

    /// <summary>Read plus written.</summary>
    public double DiskKBps { get; init; }
}

/// <summary>A user account of the device and its sign-in, when it has one.</summary>
public sealed class UserAccountDto
{
    public string UserName { get; init; } = string.Empty;

    public string? FullName { get; init; }

    /// <summary>The computer name for local accounts, otherwise the domain.</summary>
    public string? Domain { get; init; }

    public bool IsLocal { get; init; }

    public bool Enabled { get; init; } = true;

    public bool IsAdmin { get; init; }

    public bool IsSignedIn { get; init; }

    /// <summary>The signed-in user who is using the device now (the active session with the least idle time).</summary>
    public bool IsActive { get; init; }

    /// <summary>"Active", "Disconnected", "Locked"... or empty when the user is not signed in.</summary>
    public string? SessionState { get; init; }

    /// <summary>"Console", "Remote Desktop", "SSH"...</summary>
    public string? SessionType { get; init; }

    /// <summary>The computer a remote session comes from.</summary>
    public string? ClientName { get; init; }

    public DateTime? SignedInUtc { get; init; }

    public int? IdleSeconds { get; init; }

    public DateTime? LastLogonUtc { get; init; }

    public int? LogonCount { get; init; }

    public DateTime? PasswordLastSetUtc { get; init; }

    public string? ProfilePath { get; init; }

    public string? Description { get; init; }
}

/// <summary>A background service of the operating system.</summary>
public sealed class SystemServiceDto
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>"Running", "Stopped", "Failed", "Starting"...</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>"Automatic", "Manual", "Disabled"...</summary>
    public string StartMode { get; init; } = string.Empty;

    public string? Account { get; init; }

    public int? ProcessId { get; init; }

    public string? Path { get; init; }

    /// <summary>Healthy, Critical (failed, or stopped while it should start by itself) or Unknown (stopped on purpose).</summary>
    public string Health { get; init; } = "Unknown";

    /// <summary>Why the service counts as a problem; empty when it does not.</summary>
    public string? Problem { get; init; }
}
