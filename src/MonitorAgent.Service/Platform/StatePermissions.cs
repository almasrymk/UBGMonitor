using MonitorAgent.Shared.Security;
using System.Runtime.InteropServices;

namespace MonitorAgent.Service.Platform;

public static class StatePermissions
{
    [DllImport("libc")] private static extern uint umask(uint mask);
    public static void InitializeCreationMask() { if (!OperatingSystem.IsWindows()) umask(0x3f); }
    public static void Repair(string folder)
    {
        PrivateFile.EnsureDirectory(folder);
        foreach (var item in Directory.EnumerateFileSystemEntries(folder))
        {
            if (new FileInfo(item).LinkTarget is not null) throw new IOException("State cannot contain symbolic links; inspect it before restarting.");
            if (Directory.Exists(item)) Repair(item); else PrivateFile.Secure(item);
        }
    }
}
