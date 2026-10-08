using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MonitorAgent.Service.Licensing;

namespace MonitorAgent.Tests;

public sealed class DistributionSecurityTests
{
    private static string ShippedSettingsPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "MonitorAgent.Service", "appsettings.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Cannot locate the repository's shipped settings.");
    }

    [Fact]
    public void ShippedSettingsContainNoCredentialsOrPersonalMonitorPoints()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ShippedSettingsPath()));
        var root = document.RootElement;
        var licensing = root.GetProperty("LicensingClient");
        Assert.Equal(string.Empty, licensing.GetProperty("ClientSecret").GetString());
        Assert.Equal(string.Empty, licensing.GetProperty("ClientId").GetString());
        Assert.False(root.TryGetProperty("Setting", out _));
        Assert.False(root.TryGetProperty("MonitorPoints", out _));
        Assert.Equal(string.Empty, root.GetProperty("Routing").GetProperty("MadkhalServerUrl").GetString());
        Assert.Equal(string.Empty, root.GetProperty("Routing").GetProperty("CentralApiUrl").GetString());
    }

    [Fact]
    public void DeveloperCredentialsRemainConfigurableWithoutChangingProductCode()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LicensingClient:ClientId"] = "fixture-client",
            ["LicensingClient:ClientSecret"] = "fixture-only-value"
        }).Build();
        var options = LicensingOptions.FromConfiguration(configuration);
        Assert.Equal("fixture-client", options.ClientId);
        Assert.Equal("fixture-only-value", options.ClientSecret);
        Assert.Equal(LicensingOptions.DefaultProductCode, options.ProductCode);
    }
}
