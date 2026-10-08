using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Tests;

public sealed class LocalConfigCacheSecurityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "monitor-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalConfigCache _cache;
    public LocalConfigCacheSecurityTests()
    {
        Directory.CreateDirectory(_directory);
        var settings = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(settings, "{}");
        _cache = new LocalConfigCache(new AgentIdentity(Options.Create(new AgentOptions())), new ConfigurationBuilder().Build(),
            NullLogger<LocalConfigCache>.Instance, Path.Combine(_directory, "config.json"), settings);
    }

    [Fact]
    public async Task Clean_install_without_Setting_has_zero_monitor_points()
        => Assert.Empty((await _cache.GetConfigAsync()).MonitorPoints);

    [Theory]
    [InlineData("main-point-a", "regional-point-b", "remote-point-c")]
    [InlineData("camera-01", null, null)]
    public async Task Existing_sample_cache_becomes_empty_and_stays_empty_on_restart(string first, string? second, string? third)
    {
        await _cache.SaveConfigAsync(new AgentRuntimeConfig
        {
            ConfigVersion = "saved", AgentId = "existing-agent", CpuCriticalThreshold = 71,
            MonitorPoints = new[] { first, second, third }.Where(id => id is not null).Select(id => new MonitorPoint { MonitorPointId = id! }).ToList()
        });
        var loaded = await _cache.GetConfigAsync();
        Assert.Empty(loaded.MonitorPoints);
        Assert.Equal("saved", loaded.ConfigVersion);
        Assert.Equal("existing-agent", loaded.AgentId);
        Assert.Equal(71, loaded.CpuCriticalThreshold);
        Assert.Empty((await _cache.GetConfigAsync()).MonitorPoints);
    }

    [Fact]
    public async Task Real_monitor_points_and_existing_settings_survive_cache_reload()
    {
        await _cache.SaveConfigAsync(new AgentRuntimeConfig
        {
            ConfigVersion = "customer-version", DatabaseConnectionString = "Data Source=fixture.db",
            MonitorPoints = [new MonitorPoint { MonitorPointId = "customer-point", Address = "https://fixture.test", Enabled = true }]
        });
        var loaded = await _cache.GetConfigAsync();
        Assert.Equal("customer-point", Assert.Single(loaded.MonitorPoints).MonitorPointId);
        Assert.Equal("https://fixture.test", loaded.MonitorPoints[0].Address);
        Assert.Equal("customer-version", loaded.ConfigVersion);
        Assert.Equal("Data Source=fixture.db", loaded.DatabaseConnectionString);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
