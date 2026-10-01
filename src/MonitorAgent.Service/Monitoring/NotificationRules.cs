using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Service.Monitoring;

/// <summary>Turns hardware rows, sensors and monitor point statuses into the notifications shown by the app.</summary>
public static class NotificationRules
{
    public static IEnumerable<AgentIssueDto> HardwareOs(
        DeviceSpecSettings spec, IEnumerable<HardwareItemDto> rows, bool osSpecBad, string operatingSystem)
    {
        var osReported = false;
        foreach (var row in rows.Where(row => DeviceSpecEvaluator.RowMeetsSpec(spec, row.Name, row.Value) == false))
        {
            switch (row.Name)
            {
                case "CPU Cores":
                    yield return Issue($"hw:{row.Name}", "Critical", "Hardware & OS: not enough CPU cores",
                        $"This device has {row.Value} CPU cores, but Device Specifications require at least {spec.CpuMinCores}. " +
                        "Upgrade the processor or lower the minimum in Settings > Device Specifications.");
                    break;
                case "RAM Total":
                    yield return Issue($"hw:{row.Name}", "Critical", "Hardware & OS: not enough memory (RAM)",
                        $"This device has {row.Value} of RAM, but Device Specifications require at least {spec.RamMinGb:0.#} GB. " +
                        "Add more memory or lower the minimum in Settings > Device Specifications.");
                    break;
                case "Windows Edition":
                    osReported = true;
                    if (spec.OsNotify)
                    {
                        yield return OsIssue(spec, row.Value);
                    }

                    break;
                default:
                    yield return Issue($"hw:{row.Name}", "Critical", $"Hardware & OS: {row.Name} does not match",
                        $"{row.Name} is \"{row.Value}\", which does not meet the minimum in Settings > Device Specifications.");
                    break;
            }
        }

        if (osSpecBad && !osReported && spec.OsNotify)
        {
            yield return OsIssue(spec, operatingSystem);
        }
    }

    /// <summary>Temperatures the sensors level marks red (above 85°C).</summary>
    public static IEnumerable<AgentIssueDto> Sensors(IEnumerable<HardwareItemDto> rows)
    {
        foreach (var row in rows)
        {
            if (row.Status != "Red"
                || !row.Name.Contains("Temp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Value, "-", StringComparison.Ordinal))
            {
                continue;
            }

            var part = row.Name.Replace("Temp", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            yield return Issue($"sensor:{row.Name}", "Critical", $"High temperature: {part} ({row.Value})",
                $"The {part} temperature is {row.Value}, above the safe limit of 85°C (Live Sensors shows it in red). " +
                "Check that the fans are working, the air vents are not blocked, and the device is not covered with dust. " +
                "The device may slow down or shut down to protect itself.");
        }
    }

    public static IEnumerable<AgentIssueDto> MonitorPoints(IEnumerable<MonitorPointStatusDto> points)
    {
        foreach (var point in points.Where(point => point.Enabled))
        {
            var severity = Severity(point.Status);
            if (!Notifies(point.Alert, severity))
            {
                continue;
            }

            yield return new AgentIssueDto
            {
                Id = $"point:{point.MonitorPointId}",
                Severity = severity,
                Title = PointTitle(point, severity),
                Message = PointMessage(point, severity),
                TimestampUtc = point.LastCheckedUtc ?? DateTime.UtcNow,
                MonitorPointId = point.MonitorPointId
            };
        }
    }

    /// <summary>Problem notifies red only, Warning adds yellow, Unknown adds gray as well.</summary>
    private static bool Notifies(MonitorPointAlert alert, string severity) => severity switch
    {
        "Critical" => true,
        "Warning" => alert is MonitorPointAlert.Warning or MonitorPointAlert.Unknown,
        "Unknown" => alert == MonitorPointAlert.Unknown,
        _ => false
    };

    private static string Severity(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        if (status.Contains("Critical", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Offline", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Down", StringComparison.OrdinalIgnoreCase))
        {
            return "Critical";
        }

        return status.Contains("Warning", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Healthy";
    }

    private static string TypeName(MonitorPointType type) => type switch
    {
        MonitorPointType.Website => "Website",
        MonitorPointType.Application => "Application",
        MonitorPointType.Database => "Database",
        MonitorPointType.Device => "Device",
        MonitorPointType.Madkhal => "Madkhal server",
        MonitorPointType.Agent => "Agent",
        _ => "Monitor point"
    };

    private static string PointTitle(MonitorPointStatusDto point, string severity)
    {
        var type = TypeName(point.Type);
        return severity switch
        {
            "Critical" => point.Type == MonitorPointType.Application
                ? $"Application not running: {point.DisplayName}"
                : $"{type} unreachable: {point.DisplayName}",
            "Warning" => $"{type} warning: {point.DisplayName}",
            _ => $"{type} status unknown: {point.DisplayName}"
        };
    }

    private static string PointMessage(MonitorPointStatusDto point, string severity)
    {
        var type = TypeName(point.Type);
        var name = $"{type} \"{point.DisplayName}\"";
        var address = string.IsNullOrWhiteSpace(point.Address) ? string.Empty : $" ({point.Address.Trim()})";
        var reason = string.IsNullOrWhiteSpace(point.Message) ? string.Empty : $" Reason: {point.Message.Trim().TrimEnd('.')}.";
        var location = string.IsNullOrWhiteSpace(point.Location) ? string.Empty : $" Location: {point.Location.Trim()}.";
        var checkedAt = point.LastCheckedUtc is DateTime utc ? $" Last checked at {utc.ToLocalTime():HH:mm:ss}." : string.Empty;

        var body = severity switch
        {
            "Critical" => point.Type switch
            {
                MonitorPointType.Application =>
                    $"{name} is not running on this device.{reason} Start the application or check why it closed.",
                MonitorPointType.Device =>
                    $"{name}{address} does not respond to ping.{reason} Check that it is powered on and connected to the network.",
                MonitorPointType.Website =>
                    $"{name}{address} cannot be reached.{reason} Check the address, the internet connection, or the website server.",
                MonitorPointType.Database =>
                    $"{name}{address} cannot be connected.{reason} Check that the database server is running and the login details are correct.",
                _ => $"{name}{address} is not available.{reason}"
            },
            "Warning" => $"{name}{address} is working but has a warning.{reason}",
            _ => $"No check result has been received yet for {name}{address}. " +
                 "Make sure the Agent service is running and the monitor point is set up correctly."
        };

        return body + location + checkedAt;
    }

    private static AgentIssueDto OsIssue(DeviceSpecSettings spec, string actual) =>
        Issue("hw:os", "Critical", "Hardware & OS: operating system too old",
            $"This device runs \"{actual}\", but Device Specifications require {spec.OperatingSystem.Trim()} or newer. " +
            "Upgrade Windows or change the required operating system in Settings > Device Specifications.");

    private static AgentIssueDto Issue(string id, string severity, string title, string message) => new()
    {
        Id = id,
        Severity = severity,
        Title = title,
        Message = message,
        TimestampUtc = DateTime.UtcNow
    };
}
