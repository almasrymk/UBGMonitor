using System.Text.Json.Serialization;

namespace ClientAgent.Shared.Models;

/// <summary>Settings › Device Specifications (stored under Setting:DeviceSpec in the service appsettings.json).</summary>
public sealed class DeviceSpecSettings
{
    public int CpuMinCores { get; set; } = 4;

    public int CpuWarningPercent { get; set; } = 85;

    public int CpuProblemPercent { get; set; } = 95;

    public double RamMinGb { get; set; } = 8;

    public int RamWarningPercent { get; set; } = 85;

    public int RamProblemPercent { get; set; } = 95;

    public string DiskUnit { get; set; } = "Percent";

    public double DiskMinimum { get; set; } = 128;

    public double DiskTotalWarning { get; set; } = 90;

    public double DiskPartitionWarning { get; set; } = 90;

    public string DiskPartitionUnit { get; set; } = "Percent";

    public int DiskWarningPercent { get; set; }

    public double DiskRemainingWarning { get; set; } = 15;

    public string DiskRemainingWarningUnit { get; set; } = "Percent";

    public double DiskRemainingProblem { get; set; } = 5;

    public string DiskRemainingProblemUnit { get; set; } = "Percent";

    public double DownloadMinKbps { get; set; } = 10000;

    public double UploadMinKbps { get; set; } = 2000;

    /// <summary>Minimum internet download speed in Kbps; null in settings saved before Kbps was introduced.</summary>
    public double? InternetMinKbps { get; set; }

    /// <summary>Legacy Mbps value, kept in sync so older builds read the same minimum.</summary>
    public double InternetMinMbps { get; set; } = 10;

    [JsonIgnore]
    public double EffectiveInternetMinKbps => InternetMinKbps ?? InternetMinMbps * 1000d;

    public string OperatingSystem { get; set; } = "Windows 10";

    public MonitorPointAlert CpuAlert { get; set; } = MonitorPointAlert.Problem;

    public MonitorPointAlert RamAlert { get; set; } = MonitorPointAlert.Problem;

    public bool InternetNotify { get; set; } = true;

    public bool DownloadNotify { get; set; } = true;

    public bool UploadNotify { get; set; } = true;

    public MonitorPointAlert DiskAlert { get; set; } = MonitorPointAlert.Problem;

    public bool OsNotify { get; set; } = true;
}
