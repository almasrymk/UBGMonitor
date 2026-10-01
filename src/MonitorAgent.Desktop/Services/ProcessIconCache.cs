using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MonitorAgent.Desktop.Services;

namespace MonitorAgent.UI.Services;

/// <summary>
/// The icon of a process's program, as the WPF app shows it. Linux and macOS executables carry no icon of their
/// own, so there (and for processes Windows will not open) the row shows the same small gray square.
/// </summary>
internal static class ProcessIconCache
{
    private const int Size = 16;
    private static readonly Lazy<Bitmap?> Fallback = new(CreateFallback);
    private static readonly ConcurrentDictionary<string, Bitmap?> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, Bitmap?> ByPid = new();

    public static object? Get(int pid, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fallback.Value;
        }

        if (ByPid.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var path = TryGetPath(pid, name);
        var icon = string.IsNullOrWhiteSpace(path) ? null : ByPath.GetOrAdd(path, LoadFromPath);
        icon ??= Fallback.Value;
        ByPid[pid] = icon;
        return icon;
    }

    private static string? TryGetPath(int pid, string name)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var fileName = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName) && File.Exists(fileName))
            {
                return fileName;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Access denied for some system processes.
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.SystemDirectory, name + ".exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name + ".exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The associated icon scaled to 16×16, like the WPF app.</summary>
    private static Bitmap? LoadFromPath(string path)
        => OperatingSystem.IsWindows() ? WindowsIcons.Associated(path, Size) : null;

    private static Bitmap? CreateFallback()
    {
        try
        {
            var pixels = new byte[Size * Size * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x72;
                pixels[i + 1] = 0x6A;
                pixels[i + 2] = 0x6A;
                pixels[i + 3] = 0xFF;
            }

            var bitmap = new WriteableBitmap(new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
            {
                for (var row = 0; row < Size; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels, row * Size * 4, buffer.Address + row * buffer.RowBytes, Size * 4);
                }
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
