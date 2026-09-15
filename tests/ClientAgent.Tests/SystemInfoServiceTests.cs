using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class SystemInfoServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_DoesNotThrow()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance, new HardwareMonitorReader());

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
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance, new HardwareMonitorReader());

        var processes = await sut.GetTopProcessesAsync(5);

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Cpu_ReturnsRequestedCountOrLess()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance, new HardwareMonitorReader());

        var processes = await sut.GetTopProcessesSortedAsync(5, "cpu");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("%", item.Unit));
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Ram_UsesMegabytes()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance, new HardwareMonitorReader());

        var processes = await sut.GetTopProcessesSortedAsync(5, "ram");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("MB", item.Unit));
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Network_UsesKilobytesPerSecond()
    {
        var sut = new SystemInfoService(NullLogger<SystemInfoService>.Instance, new HardwareMonitorReader());

        var processes = await sut.GetTopProcessesSortedAsync(5, "network");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("KB/s", item.Unit));
    }
}
