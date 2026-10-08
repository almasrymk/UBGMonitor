using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MonitorAgent.Cloud;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Service.SystemInfo;

namespace MonitorAgent.Service.Cloud;

/// <summary>
/// The agent's existing collectors seen through <see cref="ICloudAgentSource"/> (AG-3): the cloud gets the same readings,
/// problems and monitor points as the local app; nothing is measured twice by a second code path.
/// </summary>
public sealed class ServiceCloudSource(
    ISystemInfoService systemInfo,
    IDiskActivityService diskActivity,
    INetworkService network,
    IApplicationsService applications,
    IMonitorHealthStore health,
    ILocalConfigCache config,
    IAgentIdentity identity,
    IOptions<LicensingOptions> licensing) : ICloudAgentSource
{
    private static readonly string[] TopKinds = ["cpu", "ram", "disk", "network"];

    public string Fingerprint => DeviceFingerprint.Get(licensing.Value.ProductCode);

    public CloudHost Host()
    {
        var family = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Other";
        var (ip, mac) = PrimaryAddress();
        return new CloudHost(
            Environment.MachineName, family, RuntimeInformation.OSDescription, Environment.OSVersion.Version.ToString(), RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            identity.Version, ip, mac, DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64));
    }

    public async Task<CloudSample> SampleAsync(CancellationToken cancellationToken)
    {
        var cpu = await systemInfo.GetCpuAsync(cancellationToken);
        var ram = await systemInfo.GetRamAsync(cancellationToken);
        var partitions = await systemInfo.GetPartitionsAsync(cancellationToken);
        var net = await network.GetNetworkAsync(cancellationToken);
        var disk = diskActivity.GetActivity();
        return new CloudSample(
            DateTimeOffset.UtcNow,
            cpu.UsagePercent,
            ram.UsagePercent,
            partitions.Count == 0 ? 0 : partitions.Max(p => p.UsagePercent),
            (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds,
            disk?.ActiveTimePercent,
            disk is null ? null : (long)disk.ReadBytesPerSecond,
            disk is null ? null : (long)disk.WriteBytesPerSecond,
            disk?.ResponseMs,
            (long)(net.DownloadMbps * 125_000),
            (long)(net.UploadMbps * 125_000),
            net.PingMs,
            net.PacketLossPercent,
            cpu.TemperatureC);
    }

    public async Task<IReadOnlyList<CloudDisk>> DisksAsync(CancellationToken cancellationToken) =>
        (await systemInfo.GetPartitionsAsync(cancellationToken))
            .Select(p => new CloudDisk(p.DriveLetter.TrimEnd('\\'), p.Label, p.FileSystem, Math.Round(p.TotalGB, 2), Math.Round(p.UsedGB, 2), Math.Round(p.FreeGB, 2)))
            .ToList();

    public IReadOnlyList<CloudIssue> Issues() =>
        health.GetIssues()
            .Select(i => new CloudIssue(i.Id, i.Severity, i.Title, i.Message, new DateTimeOffset(DateTime.SpecifyKind(i.TimestampUtc, DateTimeKind.Utc)), i.MonitorPointId))
            .ToList();

    public IReadOnlyList<CloudPoint> MonitorPoints()
    {
        var points = config.GetConfigAsync().GetAwaiter().GetResult().MonitorPoints;
        return points
            .Where(p => !string.IsNullOrWhiteSpace(p.MonitorPointId))
            .Select(p => new CloudPoint(
                p.MonitorPointId, string.IsNullOrWhiteSpace(p.DisplayName) ? p.MonitorPointId : p.DisplayName, p.Type.ToString(), p.Address, p.Enabled,
                health.GetStatus(p.MonitorPointId) ?? "Unknown", health.GetMessage(p.MonitorPointId), health.GetResponseMs(p.MonitorPointId),
                Utc(health.GetLastCheckedUtc(p.MonitorPointId)), Utc(health.GetStatusSinceUtc(p.MonitorPointId)), p.IntervalSeconds))
            .ToList();
    }

    public async Task<object?> SnapshotAsync(CancellationToken cancellationToken)
    {
        var cpu = await systemInfo.GetCpuAsync(cancellationToken);
        var ram = await systemInfo.GetRamAsync(cancellationToken);
        var partitions = await systemInfo.GetPartitionsAsync(cancellationToken);
        var net = await network.GetNetworkAsync(cancellationToken);
        var disk = diskActivity.GetActivity();
        var top = new Dictionary<string, object>();
        foreach (var kind in TopKinds)
        {
            try
            {
                top[kind] = (await systemInfo.GetTopProcessesSortedAsync(5, kind, cancellationToken)).Select(p => new { name = p.Name, pid = p.Pid, value = Math.Round(p.Value, 1) }).ToList();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                top[kind] = Array.Empty<object>();
            }
        }

        return new
        {
            capturedAt = DateTimeOffset.UtcNow,
            cpu = new { usage = Math.Round(cpu.UsagePercent, 1), physicalCores = cpu.PhysicalCores, logicalCores = cpu.LogicalCores, speedGhz = cpu.CurrentSpeedGhz, tempC = cpu.TemperatureC, processes = cpu.ProcessCount, model = cpu.Model },
            ram = new { usage = Math.Round(ram.UsagePercent, 1), totalGb = ram.TotalGB, usedGb = ram.UsedGB, freeGb = ram.FreeGB, cachedGb = ram.CachedGB },
            diskActivity = disk is null ? null : new { activePercent = disk.ActiveTimePercent, readBps = disk.ReadBytesPerSecond, writeBps = disk.WriteBytesPerSecond, responseMs = disk.ResponseMs },
            partitions = partitions.Select(p => new { drive = p.DriveLetter.TrimEnd('\\'), totalGb = p.TotalGB, usedGb = p.UsedGB, freeGb = p.FreeGB, usage = p.UsagePercent }),
            network = new { adapter = net.AdapterName, downloadBps = net.DownloadMbps * 125_000, uploadBps = net.UploadMbps * 125_000, publicIp = net.PublicIp, localIp = net.IpAddress, pingMs = net.PingMs, lossPercent = net.PacketLossPercent },
            top,
            service = new { status = "Running", uptimeSeconds = (long)identity.Uptime.TotalSeconds },
        };
    }

    public async Task<IReadOnlyDictionary<string, object>> InventoryAsync(CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, object>
        {
            ["hardware"] = await systemInfo.GetHardwareAsync(cancellationToken),
            ["os"] = await systemInfo.GetOsAsync(cancellationToken),
            ["disks"] = await systemInfo.GetPartitionsAsync(cancellationToken),
            ["sensors"] = await systemInfo.GetSensorsAsync(cancellationToken),
        };
        var net = await network.GetNetworkAsync(cancellationToken);
        // Live rates change every second; only the configuration of the network belongs to the inventory.
        documents["network"] = new { net.AdapterName, net.ConnectionType, net.IpAddress, net.MacAddress, net.Gateway, net.DnsServers, net.LinkSpeedMbps, net.WifiSsid };
        documents["programs"] = await applications.GetInstalledProgramsAsync(cancellationToken);
        documents["services"] = await applications.GetServicesAsync(cancellationToken);
        documents["users"] = await applications.GetUsersAsync(cancellationToken);
        return documents;
    }

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;

    private static (string? Ip, string? Mac) PrimaryAddress()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count)
                .FirstOrDefault();
            var ip = nic?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
            var mac = nic?.GetPhysicalAddress().ToString();
            return (ip, string.IsNullOrEmpty(mac) ? null : string.Join('-', Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2))));
        }
        catch (NetworkInformationException)
        {
            return (null, null);
        }
    }
}
