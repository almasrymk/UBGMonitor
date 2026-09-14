using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class SystemInfoServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_DoesNotThrow()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance);

        var snapshot = await sut.GetSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Cpu.LogicalCores >= 0);
        Assert.NotNull(snapshot.Ram);
        Assert.NotNull(snapshot.Partitions);
        Assert.NotNull(snapshot.Network);
        Assert.NotNull(snapshot.Hardware);
        Assert.NotNull(snapshot.Os);
    }

    [Fact]
    public async Task GetTopProcessesAsync_ReturnsRequestedCountOrLess()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance);

        var processes = await sut.GetTopProcessesAsync(5);

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
    }
}
