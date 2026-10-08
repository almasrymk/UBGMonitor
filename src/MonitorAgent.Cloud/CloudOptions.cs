using Microsoft.Extensions.Configuration;

namespace MonitorAgent.Cloud;

/// <summary>
/// The <c>Cloud</c> settings section (05 section 10). <see cref="BaseUrl"/>, <see cref="ProductKey"/> and
/// <see cref="LocationCode"/> can also come from the installer: <c>MONITORAGENT_CLOUDURL</c>,
/// <c>MONITORAGENT_PRODUCTKEY</c> and <c>MONITORAGENT_LOCATION</c>.
/// </summary>
public sealed class CloudOptions
{
    public const string SectionName = "Cloud";

    /// <summary>Off by default: an agent without a cloud keeps working locally exactly as before.</summary>
    public bool Enabled { get; set; }

    /// <summary>The cloud's HTTPS address (enrollment and token).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Overrides the gateway address returned by enrollment (tests, proxies).</summary>
    public string? GatewayUrl { get; set; }

    /// <summary>Used once, to enroll on the first start; never written back to the settings.</summary>
    public string? ProductKey { get; set; }

    public string? LocationCode { get; set; }

    /// <summary>Remote actions (M11) stay off unless this device allows them.</summary>
    public bool AllowRemoteActions { get; set; }

    /// <summary>Seconds between samples for the minute aggregates (AG-6).</summary>
    public int SampleSeconds { get; set; } = 5;

    /// <summary>Outbox cap (AG-5); the oldest metric rows go first.</summary>
    public long MaxOutboxBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Folder of <c>cloud.json</c> and <c>cloud.db</c> (set by the service: the agent's state folder).</summary>
    public string StateFolder { get; set; } = string.Empty;

    public static CloudOptions FromConfiguration(IConfiguration configuration, string stateFolder)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new CloudOptions();
        configuration.GetSection(SectionName).Bind(options);
        options.BaseUrl = Environment.GetEnvironmentVariable("MONITORAGENT_CLOUDURL") is { Length: > 0 } url ? url : options.BaseUrl;
        options.ProductKey = Environment.GetEnvironmentVariable("MONITORAGENT_PRODUCTKEY") is { Length: > 0 } key ? key : options.ProductKey;
        options.LocationCode = Environment.GetEnvironmentVariable("MONITORAGENT_LOCATION") is { Length: > 0 } location ? location : options.LocationCode;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && Environment.GetEnvironmentVariable("MONITORAGENT_CLOUDURL") is { Length: > 0 })
            options.Enabled = true;
        options.StateFolder = string.IsNullOrWhiteSpace(options.StateFolder) ? stateFolder : options.StateFolder;
        options.SampleSeconds = Math.Clamp(options.SampleSeconds, 1, 30);
        return options;
    }
}
