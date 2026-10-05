using System.Diagnostics;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>A program as the system registers it; <see cref="InstallLocation"/> is its own folder, when it has one.</summary>
public sealed record InstalledProgram(
    string Name,
    string? Publisher,
    string? Version,
    DateTime? InstallDate,
    string? InstallLocation,
    string? ExecutablePath,
    double? SizeMB);

/// <summary>What is installed on this computer, who uses it, and its background services, read the way each system keeps them.</summary>
public interface IHostInventory
{
    IReadOnlyList<InstalledProgram> ReadPrograms();

    IReadOnlyList<UserAccountDto> ReadUsers();

    IReadOnlyList<SystemServiceDto> ReadServices();

    /// <summary>The full path of a process's executable; null when the system does not tell.</summary>
    string? ProcessPath(int pid);
}

public class BasicHostInventory : IHostInventory
{
    public virtual IReadOnlyList<InstalledProgram> ReadPrograms() => [];

    public virtual IReadOnlyList<UserAccountDto> ReadUsers() => [];

    public virtual IReadOnlyList<SystemServiceDto> ReadServices() => [];

    public virtual string? ProcessPath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
