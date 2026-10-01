using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorAgent.UI.Services;

public static partial class SiteLogoCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> Images = new(StringComparer.Ordinal);

    public static ImageSource? ToImage(string? icon)
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

    private static partial bool CanDecode(byte[] bytes) => Decode(bytes) is not null;

    private static ImageSource? Decode(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        if (!dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Decode(bytes));
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.DecodePixelWidth = 64;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
