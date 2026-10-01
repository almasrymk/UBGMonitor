using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.UI.Services;

public static partial class SiteLogoCache
{
    private const int MaxBytes = 1_000_000;

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    static SiteLogoCache()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <summary>True when the UI framework can show the image.</summary>
    private static partial bool CanDecode(byte[] bytes);

    public static async Task<string?> CaptureAsync(string? address)
    {
        if (!TryPage(address, MonitorPointType.Website, out var page))
        {
            return null;
        }

        try
        {
            var bytes = await DiscoverAsync(page);
            return bytes is null ? null : Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DiscoverAsync(Uri page)
    {
        var candidates = new List<string>();
        try
        {
            using var response = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (media.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    !media.Contains("svg", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await ReadLimitedAsync(response);
                    if (bytes is not null && CanDecode(bytes))
                    {
                        return bytes;
                    }
                }
                else if (media.Length == 0 ||
                         media.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                         media.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    var html = await ReadTextLimitedAsync(response);
                    if (!string.IsNullOrEmpty(html))
                    {
                        candidates.AddRange(ExtractIcons(html));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
        }

        var origin = page.GetLeftPart(UriPartial.Authority);
        candidates.Add(origin + "/favicon.ico");
        candidates.Add(origin + "/apple-touch-icon.png");
        candidates.Add(origin + "/favicon.png");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tried = 0;
        foreach (var href in candidates)
        {
            if (!seen.Add(href) || tried >= 5)
            {
                continue;
            }

            tried++;
            try
            {
                var image = await FromHrefAsync(page, href);
                if (image is not null && CanDecode(image))
                {
                    return image;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or FormatException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractIcons(string html)
    {
        var found = new List<(int Rank, int Size, string Href)>();
        foreach (Match link in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = link.Value;
            var rel = Attribute(tag, "rel");
            var href = Attribute(tag, "href");
            if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!rel.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("mask-icon", StringComparison.OrdinalIgnoreCase) ||
                href.Contains(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rank = rel.Contains("apple-touch", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            found.Add((rank, ParseSize(Attribute(tag, "sizes")), href));
        }

        return found
            .OrderBy(item => item.Rank)
            .ThenByDescending(item => item.Size)
            .Select(item => item.Href);
    }

    private static async Task<byte[]?> FromHrefAsync(Uri page, string href)
    {
        if (href.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            if (href.Contains("svg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var comma = href.IndexOf(',');
            if (comma < 0)
            {
                return null;
            }

            var meta = href[..comma];
            var payload = href[(comma + 1)..];
            byte[] bytes = meta.Contains("base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return bytes.Length is > 0 and <= MaxBytes ? bytes : null;
        }

        if (!TryResolve(page, href, out var uri) ||
            uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (media.Contains("svg", StringComparison.OrdinalIgnoreCase) ||
            media.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ReadLimitedAsync(response);
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response)
    {
        var length = response.Content.Headers.ContentLength;
        if (length > MaxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (buffer.Length <= MaxBytes)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length is 0 or > MaxBytes ? null : buffer.ToArray();
    }

    private static async Task<string?> ReadTextLimitedAsync(HttpResponseMessage response)
    {
        var bytes = await ReadLimitedAsync(response);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static bool TryPage(string? address, MonitorPointType type, out Uri page)
    {
        page = null!;
        if (type != MonitorPointType.Website || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var value = address.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out page!)
            && (page.Scheme == Uri.UriSchemeHttp || page.Scheme == Uri.UriSchemeHttps);
    }

    private static bool TryResolve(Uri page, string href, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(page, href, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(
            tag,
            $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        if (match.Groups[1].Success)
        {
            return match.Groups[1].Value;
        }

        return match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    private static int ParseSize(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes))
        {
            return 0;
        }

        var match = Regex.Match(sizes, @"\d+");
        return match.Success && int.TryParse(match.Value, out var size) ? size : 0;
    }
}
