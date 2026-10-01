namespace MonitorAgent.Shared.Models;

/// <summary>Internet reachability and speed test results, measured by the service.</summary>
public sealed class InternetStateDto
{
    /// <summary>Null until the first check has finished.</summary>
    public bool? Connected { get; set; }

    public DateTime? CheckedUtc { get; set; }

    public bool SpeedTestRunning { get; set; }

    /// <summary>Progress text while a test runs ("Testing download...").</summary>
    public string? SpeedTestProgress { get; set; }

    public double? DownloadMbps { get; set; }

    public double? UploadMbps { get; set; }

    public DateTime? SpeedTestCompletedAt { get; set; }

    public string? SpeedTestError { get; set; }
}
