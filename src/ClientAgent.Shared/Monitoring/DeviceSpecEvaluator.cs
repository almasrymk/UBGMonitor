using System.Globalization;
using System.Text.RegularExpressions;
using ClientAgent.Shared.Models;

namespace ClientAgent.Shared.Monitoring;

public readonly record struct DiskSlice(string Name, double TotalGb, double FreeGb, double UsagePercent);

public readonly record struct DeviceSpecReading(
    int CpuCores,
    double CpuUsagePercent,
    double RamTotalGb,
    double RamUsagePercent,
    IReadOnlyList<DiskSlice> Partitions,
    bool InternetConnected,
    double DownloadMbps,
    double UploadMbps,
    string OperatingSystem);

public sealed class DeviceSpecResult
{
    public bool CpuOk { get; init; }
    public bool CpuBad { get; init; }
    public bool RamOk { get; init; }
    public bool RamBad { get; init; }
    public bool DiskOk { get; init; }
    public bool DiskBad { get; init; }
    public bool DownloadOk { get; init; }
    public bool DownloadBad { get; init; }
    public bool UploadOk { get; init; }
    public bool UploadBad { get; init; }
    public bool InternetOk { get; init; }
    public bool InternetBad { get; init; }
    public bool OsOk { get; init; }
    public bool OsBad { get; init; }
    public List<AgentIssueDto> Issues { get; init; } = [];
}

public static class DeviceSpecEvaluator
{
    public static DeviceSpecResult Evaluate(DeviceSpecSettings spec, DeviceSpecReading reading)
    {
        var issues = new List<AgentIssueDto>();
        var cpu = CompareMinimum(spec.CpuMinCores > 0, reading.CpuCores > 0, reading.CpuCores >= spec.CpuMinCores);
        var cpuProblem = AddPercentIssue(issues, "spec:cpu-usage", "CPU", "processor", reading.CpuUsagePercent,
            spec.CpuWarningPercent, spec.CpuProblemPercent, spec.CpuAlert,
            "Close heavy programs or check the Top 5 by CPU list on the dashboard.");

        var ram = CompareMinimum(spec.RamMinGb > 0, reading.RamTotalGb > 0, reading.RamTotalGb + 0.05 >= spec.RamMinGb);
        var ramProblem = reading.RamTotalGb > 0 && AddPercentIssue(issues, "spec:ram-usage", "RAM", "memory",
            reading.RamUsagePercent, spec.RamWarningPercent, spec.RamProblemPercent, spec.RamAlert,
            $"Total memory is {reading.RamTotalGb:0.0} GB. Close unused programs or check the Top 5 by RAM list on the dashboard.");

        var hasDisk = reading.Partitions.Count > 0;
        var totalGb = reading.Partitions.Sum(slice => slice.TotalGb);
        var disk = CompareMinimum(spec.DiskMinimum > 0, hasDisk, totalGb + 0.05 >= spec.DiskMinimum);
        if (disk == false)
        {
            issues.Add(Issue("spec:disk", "Disk capacity below minimum",
                $"Total disk capacity on this device is {totalGb:0.0} GB, but Device Specifications require at least {spec.DiskMinimum:0.0} GB. " +
                "Install a larger disk or lower the minimum in Settings > Device Specifications.", "Critical"));
        }

        var diskProblem = false;
        if (hasDisk)
        {
            var freeGb = reading.Partitions.Sum(slice => slice.FreeGb);
            diskProblem = AddRemainingIssue(issues, "spec:disk-usage", "All disks", totalGb, freeGb, spec);
            foreach (var slice in reading.Partitions)
            {
                var name = string.IsNullOrWhiteSpace(slice.Name) ? "Partition" : $"Drive {slice.Name}";
                diskProblem |= AddRemainingIssue(issues, $"spec:disk-part:{name}", name, slice.TotalGb, slice.FreeGb, spec);
            }
        }

        var diskOk = disk == true && !diskProblem;
        var diskBad = disk == false || diskProblem;

        var download = CompareSpeed(issues, "spec:download", "Download speed", reading.InternetConnected,
            reading.DownloadMbps, spec.DownloadMinKbps, spec.DownloadNotify);
        var upload = CompareSpeed(issues, "spec:upload", "Upload speed", reading.InternetConnected,
            reading.UploadMbps, spec.UploadMinKbps, spec.UploadNotify);

        bool? internet = null;
        var internetMinKbps = spec.EffectiveInternetMinKbps;
        if (internetMinKbps > 0)
        {
            var downloadKbps = reading.DownloadMbps * 1000d;
            internet = reading.InternetConnected && downloadKbps + 1 >= internetMinKbps;
            if (internet == false && spec.InternetNotify)
            {
                issues.Add(Issue("spec:internet", reading.InternetConnected ? "Internet speed below minimum" : "Internet disconnected",
                    reading.InternetConnected
                        ? $"The measured internet speed is {downloadKbps:N0} Kbps, but Device Specifications require at least {internetMinKbps:N0} Kbps. " +
                          "Check the internet line or contact the internet provider."
                        : "This device has no internet connection. Check the network cable or Wi-Fi and the router.",
                    "Critical"));
            }
        }

        var requiredOs = spec.OperatingSystem.Trim();
        bool? os = null;
        if (requiredOs.Length > 0 && !string.IsNullOrWhiteSpace(reading.OperatingSystem))
        {
            os = OperatingSystemMeets(reading.OperatingSystem, requiredOs);
        }

        return new DeviceSpecResult
        {
            CpuOk = cpu == true && !cpuProblem,
            CpuBad = cpu == false || cpuProblem,
            RamOk = ram == true && !ramProblem,
            RamBad = ram == false || ramProblem,
            DiskOk = diskOk,
            DiskBad = diskBad,
            DownloadOk = download == true,
            DownloadBad = download == false,
            UploadOk = upload == true,
            UploadBad = upload == false,
            InternetOk = internet == true,
            InternetBad = internet == false,
            OsOk = os == true,
            OsBad = os == false,
            Issues = issues
        };
    }

    /// <summary>Compares a Hardware &amp; OS row against its Device Specifications minimum, if it has one.</summary>
    public static bool? RowMeetsSpec(DeviceSpecSettings spec, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return null;
        }

        switch (label)
        {
            case "CPU Cores" when spec.CpuMinCores > 0:
                return ParseNumber(value) is double cores ? cores >= spec.CpuMinCores : null;
            case "RAM Total" when spec.RamMinGb > 0:
                return ParseSizeGb(value) is double ramGb ? ramGb + 0.05 >= spec.RamMinGb : null;
            case "Windows Edition" when !string.IsNullOrWhiteSpace(spec.OperatingSystem):
                return OperatingSystemMeets(value, spec.OperatingSystem.Trim());
            default:
                return null;
        }
    }

    private static double? ParseNumber(string text)
    {
        var match = Regex.Match(text, @"\d+(?:\.\d+)?");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ParseSizeGb(string text)
    {
        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(TB|GB|MB)?", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "TB" => value * 1024d,
            "MB" => value / 1024d,
            _ => value
        };
    }

    public static double? ParseSpeedMbps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(gbps|mbps|kbps)?", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "gbps" => value * 1000d,
            "kbps" => value / 1000d,
            _ => value
        };
    }

    private static bool OperatingSystemMeets(string actual, string required)
    {
        var actualRank = FindWindowsRank(actual);
        var requiredRank = FindWindowsRank(required);
        if (actualRank is null || requiredRank is null || actualRank.Value.Family != requiredRank.Value.Family)
        {
            return actual.Contains(required, StringComparison.OrdinalIgnoreCase);
        }

        return actualRank.Value.Rank >= requiredRank.Value.Rank;
    }

    private static (int Family, int Rank)? FindWindowsRank(string text)
    {
        (string Name, int Family, int Rank)[] versions =
        [
            ("Windows Server 2025", 1, 2025),
            ("Windows Server 2022", 1, 2022),
            ("Windows Server 2019", 1, 2019),
            ("Windows Server 2016", 1, 2016),
            ("Windows 11", 0, 11),
            ("Windows 10", 0, 10)
        ];

        foreach (var version in versions)
        {
            if (text.Contains(version.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (version.Family, version.Rank);
            }
        }

        return null;
    }

    private static bool? CompareSpeed(
        List<AgentIssueDto> issues, string id, string title, bool connected, double speedMbps, double minKbps, bool notify)
    {
        if (minKbps <= 0)
        {
            return null;
        }

        var speedKbps = speedMbps * 1000d;
        var meets = connected && speedKbps + 1 >= minKbps;
        if (!meets && notify)
        {
            issues.Add(Issue(id, connected ? $"{title} below minimum" : "Internet disconnected", connected
                ? $"The last speed test measured a {title.ToLowerInvariant()} of {speedKbps:N0} Kbps, but Device Specifications require at least {minKbps:N0} Kbps. " +
                  "Check the internet line or contact the internet provider."
                : $"The {title.ToLowerInvariant()} cannot be checked because this device has no internet connection.", "Critical"));
        }

        return meets;
    }

    private static bool? CompareMinimum(bool configured, bool known, bool meets) =>
        !configured || !known ? null : meets;

    /// <summary>Red issues always notify; yellow ones only when the Alert level is Warning or lower.</summary>
    private static bool NotifiesWarnings(MonitorPointAlert alert) => alert != MonitorPointAlert.Problem;

    private static bool AddPercentIssue(
        List<AgentIssueDto> issues, string id, string name, string noun, double usedPercent,
        int alertPercent, int problemPercent, MonitorPointAlert alert, string advice)
    {
        if (problemPercent > 0 && usedPercent + 0.05 >= problemPercent)
        {
            issues.Add(Issue(id, $"{name} usage critical ({usedPercent:0}%)",
                $"The {noun} is {usedPercent:0}% used, which reached the Problem level of {problemPercent}% in Device Specifications. " +
                $"The device may become slow or stop responding. {advice}", "Critical"));
            return true;
        }

        if (alertPercent > 0 && usedPercent + 0.05 >= alertPercent && NotifiesWarnings(alert))
        {
            issues.Add(Issue(id, $"{name} usage high ({usedPercent:0}%)",
                $"The {noun} is {usedPercent:0}% used, which reached the Warning level of {alertPercent}% in Device Specifications " +
                $"(Problem level is {problemPercent}%). {advice}"));
        }

        return false;
    }

    private static bool AddRemainingIssue(
        List<AgentIssueDto> issues, string id, string name, double totalGb, double freeGb, DeviceSpecSettings spec)
    {
        if (totalGb <= 0)
        {
            return false;
        }

        var freePercent = freeGb / totalGb * 100d;
        var space = $"{freeGb:0.0} GB free of {totalGb:0.0} GB ({freePercent:0}% free)";
        if (IsRemainingHit(spec.DiskRemainingProblem, spec.DiskRemainingProblemUnit, freeGb, freePercent))
        {
            issues.Add(Issue(id, $"{name} almost full",
                $"{name} has only {space}, which reached the Problem level of {ThresholdText(spec.DiskRemainingProblem, spec.DiskRemainingProblemUnit)} remaining. " +
                "Programs may fail to save data. Delete unneeded files or move them to another disk.",
                "Critical"));
            return true;
        }

        if (IsRemainingHit(spec.DiskRemainingWarning, spec.DiskRemainingWarningUnit, freeGb, freePercent)
            && NotifiesWarnings(spec.DiskAlert))
        {
            issues.Add(Issue(id, $"{name} low on space",
                $"{name} has {space}, which reached the Warning level of {ThresholdText(spec.DiskRemainingWarning, spec.DiskRemainingWarningUnit)} remaining. " +
                "Free up space soon to avoid problems."));
        }

        return false;
    }

    private static bool IsGigabytes(string? unit) => unit is "GB" or "Gigabytes";

    private static bool IsRemainingHit(double threshold, string? unit, double freeGb, double freePercent) =>
        threshold > 0 && (IsGigabytes(unit) ? freeGb : freePercent) <= threshold + 0.05;

    private static string ThresholdText(double threshold, string? unit) =>
        IsGigabytes(unit) ? $"{threshold:0.0} GB" : $"{threshold:0}%";

    private static AgentIssueDto Issue(string id, string title, string message, string severity = "Warning") => new()
    {
        Id = id,
        Severity = severity,
        Title = title,
        Message = message,
        TimestampUtc = DateTime.UtcNow
    };
}
