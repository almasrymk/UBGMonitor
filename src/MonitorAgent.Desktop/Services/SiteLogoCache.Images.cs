using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace MonitorAgent.UI.Services;

public static partial class SiteLogoCache
{
    private static readonly ConcurrentDictionary<string, Bitmap> Images = new(StringComparer.Ordinal);

    public static Bitmap? ToImage(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        if (Images.TryGetValue(icon, out var cached))
        {
            return cached;
        }

        try
        {
            var image = Decode(Convert.FromBase64String(icon));
            if (image is not null)
            {
                Images[icon] = image;
            }

            return image;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static partial bool CanDecode(byte[] bytes)
    {
        using var image = Decode(bytes);
        return image is not null;
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, 64);
        }
        catch (Exception)
        {
            // Skia throws several exception types for data that is not an image.
            return null;
        }
    }
}
