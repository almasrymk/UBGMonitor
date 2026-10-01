using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MonitorAgent.Desktop.Services;

/// <summary>Program icons read from Windows, as the WPF app gets them through System.Drawing.Icon.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsIcons
{
    /// <summary>The icon Explorer shows for the file (Icon.ExtractAssociatedIcon), optionally scaled to a square.</summary>
    public static WriteableBitmap? Associated(string path, int? size = null)
    {
        var index = (ushort)0;
        return FromHandle(ExtractAssociatedIcon(IntPtr.Zero, new StringBuilder(path, 260), ref index), size);
    }

    /// <summary>The file's first icon at the given size (Icon.ExtractIcon).</summary>
    public static WriteableBitmap? Extract(string path, int size)
        => SHDefExtractIcon(path, 0, 0, out var large, IntPtr.Zero, (uint)size) == 0 ? FromHandle(large, null) : null;

    public static WriteableBitmap NewBitmap(int width, int height, byte[] bgra, AlphaFormat alpha)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, alpha);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(bgra, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
        }

        return bitmap;
    }

    private static WriteableBitmap? FromHandle(IntPtr hIcon, int? size)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!GetIconInfo(hIcon, out var info))
            {
                return null;
            }

            try
            {
                var color = ReadBits(info.hbmColor, out var width, out var height);
                if (color is null)
                {
                    return null;
                }

                var hasAlpha = false;
                for (var i = 3; i < color.Length && !hasAlpha; i += 4)
                {
                    hasAlpha = color[i] != 0;
                }

                if (!hasAlpha)
                {
                    // An old-style icon: the mask marks the transparent pixels.
                    var mask = ReadBits(info.hbmMask, out _, out _);
                    for (var i = 0; i < color.Length; i += 4)
                    {
                        color[i + 3] = mask is not null && mask[i] != 0 ? (byte)0 : (byte)0xFF;
                    }
                }

                Premultiply(color);
                if (size is int target && (target != width || target != height))
                {
                    color = Scale(color, width, height, target);
                    width = height = target;
                }

                return NewBitmap(width, height, color, AlphaFormat.Premul);
            }
            finally
            {
                DeleteObject(info.hbmColor);
                DeleteObject(info.hbmMask);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ExternalException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            for (var c = 0; c < 3; c++)
            {
                bgra[i + c] = (byte)((bgra[i + c] * alpha + 127) / 255);
            }
        }
    }

    /// <summary>Area-averaged resize of premultiplied pixels to a size × size square.</summary>
    private static byte[] Scale(byte[] source, int width, int height, int size)
    {
        var result = new byte[size * size * 4];
        Span<int> sum = stackalloc int[4];
        for (var y = 0; y < size; y++)
        {
            var y0 = y * height / size;
            var y1 = Math.Max(y0 + 1, (y + 1) * height / size);
            for (var x = 0; x < size; x++)
            {
                var x0 = x * width / size;
                var x1 = Math.Max(x0 + 1, (x + 1) * width / size);
                sum.Clear();
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var i = (sy * width + sx) * 4;
                        for (var c = 0; c < 4; c++)
                        {
                            sum[c] += source[i + c];
                        }
                    }
                }

                var count = (y1 - y0) * (x1 - x0);
                var o = (y * size + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    result[o + c] = (byte)(sum[c] / count);
                }
            }
        }

        return result;
    }

    private static byte[]? ReadBits(IntPtr hBitmap, out int width, out int height)
    {
        width = height = 0;
        if (hBitmap == IntPtr.Zero || GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0)
        {
            return null;
        }

        width = bm.bmWidth;
        height = bm.bmHeight;
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32
        };
        var pixels = new byte[width * height * 4];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref header, 0) == 0 ? null : pixels;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, StringBuilder iconPath, ref ushort index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, IntPtr small, uint size);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
