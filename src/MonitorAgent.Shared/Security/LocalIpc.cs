using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;

namespace MonitorAgent.Shared.Security;

public enum AgentAccessRole { None, Viewer, Administrator }
public sealed record RequiredAgentRole(AgentAccessRole Role);

public static class LocalIpc
{
    public const string ViewerPipe = "MonitorAgent.Viewer";
    public const string AdminPipe = "MonitorAgent.Admin";
    public static string RunDirectory => OperatingSystem.IsMacOS() ? "/var/run/monitoragent" : "/run/monitoragent";
    public static string SocketPath(AgentAccessRole role) => Path.Combine(RunDirectory, role == AgentAccessRole.Administrator ? "admin.sock" : "viewer.sock");

    public static async Task<Stream> ConnectAsync(AgentAccessRole role, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", role == AgentAccessRole.Administrator ? AdminPipe : ViewerPipe,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(3000, ct);
                var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
                if (!new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(owner)
                    && !new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Equals(owner))
                    throw new System.Security.SecurityException("The local endpoint is not owned by the MonitorAgent service.");
                return pipe;
            }
            catch { pipe.Dispose(); throw; }
        }
        var path = SocketPath(role);
        VerifyRootPath(RunDirectory, directory: true);
        VerifyRootPath(path, directory: false);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct); return new NetworkStream(socket, true); }
        catch { socket.Dispose(); throw; }
    }

    public static void VerifyRootPath(string path, bool directory)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        FileSystemInfo item = directory ? new DirectoryInfo(path) : new FileInfo(path);
        if (!item.Exists) throw new FileNotFoundException("The local service endpoint is unavailable.");
        if (item.LinkTarget is not null) throw new System.Security.SecurityException("A local IPC path cannot be a symbolic link.");
        var mode = File.GetUnixFileMode(path);
        if ((mode & UnixFileMode.OtherWrite) != 0 || directory && (mode & UnixFileMode.GroupWrite) != 0)
            throw new System.Security.SecurityException("The local IPC path has unsafe permissions.");
        var result = RunTool("/usr/bin/stat", OperatingSystem.IsMacOS() ? "-f" : "-c", "%u", path);
        if (result.Trim() != "0") throw new System.Security.SecurityException("The local IPC path must be owned by root.");
        if (!directory)
        {
            var type = RunTool("/usr/bin/stat", OperatingSystem.IsMacOS() ? "-f" : "-c", OperatingSystem.IsMacOS() ? "%HT" : "%F", path);
            if (!type.Trim().Equals("socket", StringComparison.OrdinalIgnoreCase)) throw new System.Security.SecurityException("The local IPC endpoint must be a socket.");
        }
    }

    public static void RemoveStaleSocket(string path)
    {
        VerifyRootPath(path, false);
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { probe.Connect(new UnixDomainSocketEndPoint(path)); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        { File.Delete(path); return; }
        throw new IOException("The local endpoint is already active; refusing to replace it.");
    }

    public static string RunTool(string program, params string[] arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start the IPC permissions tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000)) { process.Kill(); throw new IOException("IPC permissions tool timed out."); }
        if (process.ExitCode != 0) throw new UnauthorizedAccessException("Could not inspect or configure local IPC permissions.");
        error.GetAwaiter().GetResult();
        return output.GetAwaiter().GetResult();
    }
}
