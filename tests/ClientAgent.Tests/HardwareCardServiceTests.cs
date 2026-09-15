using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class HardwareCardServiceTests
{
    [Fact]
    public async Task GetStaticLevelsAsync_ReturnsLevels124_WithExpectedCounts()
    {
        var sut = new HardwareService(NullLogger<HardwareService>.Instance);

        var response = await sut.GetStaticLevelsAsync();

        Assert.Equal(3, response.Levels.Count);
        Assert.Equal(1, response.Levels[0].Level);
        Assert.Equal(14, response.Levels[0].ItemCount);
        Assert.Equal(14, response.Levels[0].Items.Count);
        Assert.Equal(2, response.Levels[1].Level);
        Assert.Equal(16, response.Levels[1].ItemCount);
        Assert.Equal(16, response.Levels[1].Items.Count);
        Assert.Equal(4, response.Levels[2].Level);
        Assert.Equal(24, response.Levels[2].ItemCount);
        Assert.Equal(24, response.Levels[2].Items.Count);
        Assert.Equal("Manufacturer", response.Levels[0].Items[0].Name);
        Assert.Equal("Product Key", response.Levels[2].Items[23].Name);
    }

    [Fact]
    public async Task GetLevelAsync_1_2_4_ReturnsRequestedLevel()
    {
        var sut = new HardwareService(NullLogger<HardwareService>.Instance);

        var level1 = await sut.GetLevelAsync(1);
        var level2 = await sut.GetLevelAsync(2);
        var level4 = await sut.GetLevelAsync(4);

        Assert.NotNull(level1);
        Assert.NotNull(level2);
        Assert.NotNull(level4);
        Assert.Equal(14, level1!.Items.Count);
        Assert.Equal(16, level2!.Items.Count);
        Assert.Equal(24, level4!.Items.Count);
        Assert.Null(await sut.GetLevelAsync(3));
    }

    [Fact]
    public async Task SensorsService_GetLevelAsync_Returns16Items()
    {
        var sut = new SensorsService(new HardwareMonitorReader());

        var level = await sut.GetLevelAsync();

        Assert.Equal(3, level.Level);
        Assert.Equal(16, level.ItemCount);
        Assert.Equal(16, level.Items.Count);
        Assert.Equal("CPU Temp", level.Items[0].Name);
        Assert.Equal("Disk Power-On", level.Items[15].Name);
    }

    [Fact]
    public async Task NetworkService_GetLevelAsync_Returns20Items()
    {
        var sut = new NetworkService(NullLogger<NetworkService>.Instance);

        var level = await sut.GetLevelAsync();

        Assert.Equal(5, level.Level);
        Assert.Equal(21, level.ItemCount);
        Assert.Equal(21, level.Items.Count);
        Assert.Equal("Hostname", level.Items[0].Name);
        Assert.Equal("Local IP", level.Items[8].Name);
        Assert.Equal("Public IP", level.Items[9].Name);
        Assert.Equal("Packet Loss", level.Items[20].Name);
        Assert.False(string.IsNullOrWhiteSpace(level.Items[0].Value));
    }
}
