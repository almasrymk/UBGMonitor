using System.Diagnostics;

namespace ClientAgent.Shared.Monitoring;

public readonly record struct SpeedTestResult(double DownloadMbps, double? UploadMbps, DateTime CompletedAt);

/// <summary>Measures internet throughput against public speed test servers, falling back when one refuses (e.g. HTTP 429).</summary>
public static class InternetSpeedTester
{
    private static readonly string[] DownloadUrls =
    [
        "https://speed.cloudflare.com/__down?bytes=25000000",
        "https://nbg1-speed.hetzner.com/100MB.bin",
        "https://ash-speed.hetzner.com/100MB.bin",
        "https://proof.ovh.net/files/100Mb.dat"
    ];

    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const int Streams = 2;
    private const int UploadBytesPerRequest = 5_000_000;
    private static readonly TimeSpan PhaseDuration = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static string? _lastError;

    public static async Task<SpeedTestResult> RunAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Testing download...");
        var download = await MeasureAsync(DownloadStreamAsync, cancellationToken).ConfigureAwait(false)
                       ?? throw new HttpRequestException($"Download test failed: {_lastError ?? "no data received"}");

        progress?.Report("Testing upload...");
        var upload = await MeasureAsync(UploadStreamAsync, cancellationToken).ConfigureAwait(false);
        return new SpeedTestResult(download, upload, DateTime.Now);
    }

    private static async Task<double?> MeasureAsync(
        Func<int, Action<long>, CancellationToken, Task> stream,
        CancellationToken cancellationToken)
    {
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phase.CancelAfter(PhaseDuration);

        long transferred = 0;
        var watch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Streams)
            .Select(index => RunStreamAsync(stream, index, bytes => Interlocked.Add(ref transferred, bytes), phase.Token));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        watch.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Interlocked.Read(ref transferred);
        if (bytes == 0)
        {
            return null;
        }

        var seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.001);
        return Math.Round(bytes * 8d / seconds / 1_000_000d, 1);
    }

    private static async Task RunStreamAsync(
        Func<int, Action<long>, CancellationToken, Task> stream,
        int index,
        Action<long> onBytes,
        CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested && failures < DownloadUrls.Length)
        {
            try
            {
                await stream(index + failures, onBytes, token).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                _lastError = ex.Message;
                failures++;
            }
        }
    }

    private static async Task DownloadStreamAsync(int attempt, Action<long> onBytes, CancellationToken token)
    {
        var url = DownloadUrls[attempt % DownloadUrls.Length];
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            onBytes(read);
        }
    }

    private static async Task UploadStreamAsync(int attempt, Action<long> onBytes, CancellationToken token)
    {
        _ = attempt;
        using var content = new ProgressContent(UploadBytesPerRequest, onBytes);
        using var response = await Client.PostAsync(UploadUrl, content, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private sealed class ProgressContent(int length, Action<long> onBytes) : HttpContent
    {
        private static readonly byte[] Chunk = CreateChunk();

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            var remaining = length;
            while (remaining > 0)
            {
                var size = Math.Min(Chunk.Length, remaining);
                await stream.WriteAsync(Chunk.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
                onBytes(size);
                remaining -= size;
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long size)
        {
            size = length;
            return true;
        }

        private static byte[] CreateChunk()
        {
            var chunk = new byte[65536];
            Random.Shared.NextBytes(chunk);
            return chunk;
        }
    }
}
