using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.LocalApi;

public static class IpcHostConfiguration
{
    [DllImport("libc", SetLastError = true)] private static extern uint umask(uint mask);
    public static void Configure(WebApplicationBuilder builder, AgentAccessRole role, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.SetOwner(administrators);
            security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));
            foreach (var group in role == AgentAccessRole.Administrator ? new[] { "MonitorAgent Admins" } : new[] { "MonitorAgent Admins", "MonitorAgent Viewers" })
            {
                try
                {
                    var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, group).Translate(typeof(SecurityIdentifier));
                    security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions, AccessControlType.Allow));
                }
                catch (IdentityNotMappedException) { logger.LogWarning("[IPC] Group {Group} is missing; only SYSTEM and elevated Administrators can use this endpoint.", group); }
            }
            builder.WebHost.UseNamedPipes(o => { o.CurrentUserOnly = false; o.PipeSecurity = security; });
            builder.WebHost.ConfigureKestrel(o => o.ListenNamedPipe(role == AgentAccessRole.Administrator ? LocalIpc.AdminPipe : LocalIpc.ViewerPipe));
            return;
        }
        // Restrictive creation applies before bind, including concurrent state-file creation.
        umask(0x3f); // 0077
        if (new DirectoryInfo(LocalIpc.RunDirectory).LinkTarget is not null) throw new IOException("IPC directory cannot be a symlink.");
        Directory.CreateDirectory(LocalIpc.RunDirectory);
        File.SetUnixFileMode(LocalIpc.RunDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        LocalIpc.VerifyRootPath(LocalIpc.RunDirectory, true);
        var path = LocalIpc.SocketPath(role);
        if (File.Exists(path)) LocalIpc.RemoveStaleSocket(path);
        builder.WebHost.ConfigureKestrel(o => o.ListenUnixSocket(path));
    }

    public static void Finish(AgentAccessRole role, ILogger logger)
    {
        if (OperatingSystem.IsWindows()) return;
        var path = LocalIpc.SocketPath(role);
        var group = role == AgentAccessRole.Administrator ? "monitoragent-admin" : "monitoragent";
        try
        {
            var chown = File.Exists("/usr/bin/chown") ? "/usr/bin/chown" : "/usr/sbin/chown";
            LocalIpc.RunTool(chown, "root:" + group, path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
        }
        catch (UnauthorizedAccessException)
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            logger.LogWarning("[IPC] Group {Group} is unavailable; the endpoint is root-only.", group);
        }
        LocalIpc.VerifyRootPath(path, false);
    }
}
