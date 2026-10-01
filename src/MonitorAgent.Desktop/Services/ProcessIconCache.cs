using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MonitorAgent.UI.Services;

/// <summary>
/// Linux and macOS executables carry no icon of their own, so every process row shows the same small square
/// (as the WPF app does for processes it cannot read).
/// </summary>
internal static class ProcessIconCache
{
    private static readonly Lazy<Bitmap?> Fallback = new(CreateFallback);

    public static object? Get(int pid, string name) => Fallback.Value;

    private static Bitmap? CreateFallback()
    {
        try
        {
            const int size = 16;
            var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = bitmap.Lock();
            var pixels = new byte[size * size * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x72;
                pixels[i + 1] = 0x6A;
                pixels[i + 2] = 0x6A;
                pixels[i + 3] = 0xFF;
            }

            for (var row = 0; row < size; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, row * size * 4, buffer.Address + row * buffer.RowBytes, size * 4);
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
