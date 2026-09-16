using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClientAgent.UI.Services;

internal static class ProcessIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, ImageSource?> ByPid = new();

    public static ImageSource? Get(int pid, string name)
    {
        if (ByPid.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        var path = TryGetPath(pid, name);
        ImageSource? icon = null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            icon = ByPath.GetOrAdd(path, LoadFromPath);
        }

        icon ??= CreateFallback();
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
        catch
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

    private static ImageSource LoadFromPath(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return CreateFallback();
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            source.Freeze();
            return source;
        }
        catch
        {
            return CreateFallback();
        }
    }

    private static ImageSource CreateFallback()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x72;
            pixels[i + 1] = 0x6A;
            pixels[i + 2] = 0x6A;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
