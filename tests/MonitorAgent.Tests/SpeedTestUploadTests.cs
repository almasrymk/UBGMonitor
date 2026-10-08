using System.Reflection;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Tests;

public sealed class SpeedTestUploadTests
{
    private static HttpContent Content(int length, Action<long> onBytes, CancellationToken token)
    {
        var type = typeof(InternetSpeedTester).GetNestedType("ProgressContent", BindingFlags.NonPublic)!;
        return (HttpContent)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [length, onBytes, token], null)!;
    }
    private sealed class AbortedStream(CancellationTokenSource? cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            cancellation?.Cancel();
            return ValueTask.FromException(new IOException("TEST-ONLY-aborted-transport"));
        }
    }
    [Fact]
    public async Task Aborted_write_after_phase_cancellation_is_cancellation_and_counts_no_failed_bytes()
    {
        using var cancel = new CancellationTokenSource();
        long counted = 0;
        using var content = Content(100, bytes => counted += bytes, cancel.Token);
        using var stream = new AbortedStream(cancel);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.CopyToAsync(stream));
        Assert.Equal(cancel.Token.IsCancellationRequested, error.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, counted);
    }
    [Fact]
    public async Task Real_transport_failure_is_not_swallowed_and_successful_upload_counts_all_bytes()
    {
        long counted = 0;
        using var failed = Content(100, bytes => counted += bytes, CancellationToken.None);
        using var broken = new AbortedStream(null);
        await Assert.ThrowsAsync<HttpRequestException>(() => failed.CopyToAsync(broken));
        Assert.Equal(0, counted);
        using var good = Content(100_000, bytes => counted += bytes, CancellationToken.None);
        using var destination = new MemoryStream();
        await good.CopyToAsync(destination);
        Assert.Equal(100_000, counted);
        Assert.Equal(100_000, destination.Length);
    }
}
