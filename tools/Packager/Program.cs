using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

// Builds Unix packages on any OS with correct file modes (the Windows tar cannot set the executable bit).
//   Packager tar <sourceDir> <out.tar.gz> <topFolder>   archive whose entries start with <topFolder>/
//   Packager deb <sourceDir> <out.deb>                  sourceDir holds DEBIAN/ (control, scripts) and the file tree
try
{
    switch (args)
    {
        case ["tar", var source, var output, var top]:
            WriteTarGz(source, output, top);
            break;
        case ["deb", var source, var output]:
            WriteDeb(source, output);
            break;
        default:
            Console.Error.WriteLine("Usage: Packager tar <sourceDir> <out.tar.gz> <topFolder> | Packager deb <sourceDir> <out.deb>");
            return 2;
    }

    Console.WriteLine($"Created {Path.GetFullPath(args[2])}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void WriteTarGz(string source, string output, string top)
{
    using var file = File.Create(output);
    using var gzip = new GZipStream(file, CompressionLevel.Optimal);
    WriteTar(gzip, source, top.TrimEnd('/') + "/", exclude: null);
}

static void WriteDeb(string source, string output)
{
    var debian = Path.Combine(source, "DEBIAN");
    var controlPath = Path.Combine(debian, "control");
    if (!File.Exists(controlPath))
    {
        throw new InvalidOperationException($"{controlPath} is missing.");
    }

    var installedKb = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
        .Where(path => !IsUnder(path, debian))
        .Sum(path => (new FileInfo(path).Length + 1023) / 1024);
    var control = Normalize(File.ReadAllText(controlPath)).TrimEnd('\n');
    if (!control.Contains("Installed-Size:", StringComparison.Ordinal))
    {
        control += $"\nInstalled-Size: {installedKb}";
    }

    File.WriteAllText(controlPath, control + "\n", new UTF8Encoding(false));

    using var deb = File.Create(output);
    deb.Write("!<arch>\n"u8);
    WriteArMember(deb, "debian-binary", "2.0\n"u8.ToArray());
    WriteArMember(deb, "control.tar.gz", GzipTar(debian, "./", exclude: null));
    WriteArMember(deb, "data.tar.gz", GzipTar(source, "./", exclude: debian));
}

static byte[] GzipTar(string source, string prefix, string? exclude)
{
    using var buffer = new MemoryStream();
    using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
    {
        WriteTar(gzip, source, prefix, exclude);
    }

    return buffer.ToArray();
}

static void WriteTar(Stream stream, string source, string prefix, string? exclude)
{
    using var tar = new TarWriter(stream, TarEntryFormat.Gnu, leaveOpen: true);
    var now = DateTimeOffset.UtcNow;
    tar.WriteEntry(Entry(TarEntryType.Directory, prefix, Mode(0b111_101_101), now));
    AddDirectory(tar, source, source, prefix, exclude, now);
}

static void AddDirectory(TarWriter tar, string root, string directory, string prefix, string? exclude, DateTimeOffset now)
{
    foreach (var sub in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
    {
        if (exclude is not null && IsUnder(sub, exclude))
        {
            continue;
        }

        tar.WriteEntry(Entry(TarEntryType.Directory, prefix + Relative(root, sub) + "/", Mode(0b111_101_101), now));
        AddDirectory(tar, root, sub, prefix, exclude, now);
    }

    foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
    {
        var bytes = File.ReadAllBytes(path);
        if (IsText(path, bytes))
        {
            bytes = Encoding.UTF8.GetBytes(Normalize(Encoding.UTF8.GetString(bytes)));
        }

        var entry = Entry(TarEntryType.RegularFile, prefix + Relative(root, path),
            IsExecutable(bytes) ? Mode(0b111_101_101) : Mode(0b110_100_100), now);
        entry.DataStream = new MemoryStream(bytes);
        tar.WriteEntry(entry);
    }
}

static GnuTarEntry Entry(TarEntryType type, string name, UnixFileMode mode, DateTimeOffset time) => new(type, name)
{
    Mode = mode,
    Uid = 0,
    Gid = 0,
    UserName = "root",
    GroupName = "root",
    ModificationTime = time
};

static UnixFileMode Mode(int bits) => (UnixFileMode)bits;

/// <summary>ELF and Mach-O binaries and scripts with a #! line run directly, so they need the executable bit.</summary>
static bool IsExecutable(byte[] bytes) =>
    bytes.Length >= 4 && (
        (bytes[0] == 0x7F && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        || (bytes[0] == 0xCF && bytes[1] == 0xFA && bytes[2] == 0xED && bytes[3] == 0xFE)
        || (bytes[0] == 0xFE && bytes[1] == 0xED && bytes[2] == 0xFA && bytes[3] == 0xCF)
        || (bytes[0] == 0xCA && bytes[1] == 0xFE && bytes[2] == 0xBA && bytes[3] == 0xBE)
        || (bytes[0] == (byte)'#' && bytes[1] == (byte)'!'));

/// <summary>Scripts and unit files are rejected by bash, systemd and launchd when they keep Windows line endings.</summary>
static bool IsText(string path, byte[] bytes) =>
    Path.GetExtension(path) is ".sh" or ".service" or ".desktop" or ".plist"
    || Path.GetFileName(path) is "control" or "conffiles"
    || (bytes.Length >= 2 && bytes[0] == (byte)'#' && bytes[1] == (byte)'!');

static string Normalize(string text) => text.Replace("\r\n", "\n");

static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static bool IsUnder(string path, string directory) =>
    Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);

static void WriteArMember(Stream ar, string name, byte[] data)
{
    var header = $"{name,-16}{DateTimeOffset.UtcNow.ToUnixTimeSeconds(),-12}{0,-6}{0,-6}{"100644",-8}{data.Length,-10}`\n";
    ar.Write(Encoding.ASCII.GetBytes(header));
    ar.Write(data);
    if (data.Length % 2 == 1)
    {
        ar.WriteByte((byte)'\n');
    }
}
