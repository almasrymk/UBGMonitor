using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class HardwareCardServiceTests
{
    [Fact]
    public async Task GetStaticLevelsAsync_ReturnsLevels124_WithExpectedCounts()
    {
        var sut = new HardwareService(new FakeLocalConfigCache(), NullLogger<HardwareService>.Instance);

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
        var sut = new HardwareService(new FakeLocalConfigCache(), NullLogger<HardwareService>.Instance);

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
    public async Task NetworkService_GetLevelAsync_ReturnsBaseItemsPlusWifiDetails()
    {
        var sut = new NetworkService(new FakeLocalConfigCache(), NullLogger<NetworkService>.Instance);

        var level = await sut.GetLevelAsync();

        Assert.Equal(5, level.Level);
        var expected = level.Items.Any(item => item.Name.StartsWith("Wi-Fi", StringComparison.Ordinal)) ? 28 : 21;
        Assert.Equal(expected, level.ItemCount);
        Assert.Equal(expected, level.Items.Count);
        Assert.Equal("Hostname", level.Items[0].Name);
        Assert.Contains(level.Items, item => item.Name == "Local IP");
        Assert.Contains(level.Items, item => item.Name == "Public IP");
        Assert.Contains(level.Items, item => item.Name == "Packet Loss");
        Assert.False(string.IsNullOrWhiteSpace(level.Items[0].Value));
    }
}
