using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class SystemInfoServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_DoesNotThrow()
    {
        var snapshot = await CreateSut().GetSnapshotAsync();

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
        var processes = await CreateSut().GetTopProcessesAsync(5);

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Cpu_ReturnsRequestedCountOrLess()
    {
        var processes = await CreateSut().GetTopProcessesSortedAsync(5, "cpu");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("%", item.Unit));
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Ram_UsesMegabytes()
    {
        var processes = await CreateSut().GetTopProcessesSortedAsync(5, "ram");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("MB", item.Unit));
    }

    [Fact]
    public async Task GetTopProcessesSortedAsync_Network_UsesKilobytesPerSecond()
    {
        var processes = await CreateSut().GetTopProcessesSortedAsync(5, "network");

        Assert.NotNull(processes);
        Assert.True(processes.Count <= 5);
        Assert.All(processes, item => Assert.Equal("KB/s", item.Unit));
    }

    internal static SystemInfoService CreateSut()
    {
        var reader = new HardwareMonitorReader();
        var hardware = new HardwareService(NullLogger<HardwareService>.Instance);
        var sensors = new SensorsService(reader);
        var network = new NetworkService(NullLogger<NetworkService>.Instance);
        return new SystemInfoService(NullLogger<SystemInfoService>.Instance, hardware, sensors, network);
    }
}
