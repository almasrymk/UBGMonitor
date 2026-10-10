using Microsoft.Extensions.Logging.Abstractions;
using MonitorAgent.Service.Platform;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class PerformancePolicyTests
{
    [Theory]
    [InlineData(true, 5, 5)]
    [InlineData(false, 5, 30)]
    [InlineData(false, 60, 60)]
    [InlineData(true, 0, 1)]
    public void Background_sections_refresh_less_often(bool visible, int configured, int expected)
        => Assert.Equal(expected, ApplicationsRefreshCadence.Seconds(visible, configured));

    [Fact]
    public void Icon_cache_is_bounded_and_does_not_dispose_objects_still_in_use()
    {
        var cache = new BoundedWeakCache<object>(2);
        var first = new object();
        var second = new object();
        Assert.Same(first, cache.Get("first", _ => first));
        Assert.Same(second, cache.Get("second", _ => second));
        Assert.Same(first, cache.Get("FIRST", _ => throw new Exception("Cached icon must be reused")));
        cache.Get("third", _ => new object());
        Assert.Equal(2, cache.Count);
        var loads = 0;
        cache.Get("second", _ => { loads++; return second; });
        Assert.Equal(1, loads);
        Assert.Equal(2, cache.Count);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [Fact]
    public void Concurrent_process_readers_reuse_one_snapshot()
    {
        var probe = new CountingProbe();
        var sampler = new ProcessUsageSampler(probe, new BasicHostInventory(), NullLogger<ProcessUsageSampler>.Instance);
        var first = sampler.Read();
        Parallel.For(0, 8, _ => Assert.Same(first, sampler.Read()));
        Assert.Equal(2, probe.DiskReads);
        Assert.Equal(2, probe.NetworkReads);
    }

    private sealed class CountingProbe : BasicSystemProbe
    {
        public int DiskReads;
        public int NetworkReads;
        public override Dictionary<int, ulong> ReadProcessDiskBytes() { DiskReads++; return []; }
        public override Dictionary<int, ulong> ReadProcessNetworkBytes(TimeSpan budget) { NetworkReads++; return []; }
    }
}
