using System.Text;

namespace MonitorAgent.Shared.Security;

/// <summary>Atomic replacement with owner-only creation, independent of the Unix process umask.</summary>
public static class PrivateFile
{
    public static void EnsureDirectory(string path)
    {
        if (new DirectoryInfo(path).LinkTarget is not null) throw new IOException("Private data directories cannot be symbolic links.");
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static void Secure(string path)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Private data files cannot be symbolic links.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    public static void WriteAllText(string path, string text) => WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Private data files cannot be symbolic links.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
            Secure(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string Backup(string source, string folder)
    {
        EnsureDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(source) + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N") + ".bak");
        Copy(source, destination);
        return destination;
    }
    public static void Copy(string source, string destination)
    {
        if (new FileInfo(source).LinkTarget is not null) throw new IOException("Private data files cannot be symbolic links.");
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (new FileInfo(destination).LinkTarget is not null) throw new IOException("Private data files cannot be symbolic links.");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temporary, options)) { input.CopyTo(output); output.Flush(true); }
            File.Move(temporary, destination, true);
            Secure(destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
