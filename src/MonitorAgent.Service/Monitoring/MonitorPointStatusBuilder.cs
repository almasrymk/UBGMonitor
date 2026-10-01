using MonitorAgent.Service.Config;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Service.Monitoring;

public static class MonitorPointStatusBuilder
{
    public static async Task<List<MonitorPointStatusDto>> BuildAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var config = await services.GetRequiredService<ILocalConfigCache>().GetConfigAsync(cancellationToken);
        var health = services.GetRequiredService<IMonitorHealthStore>();
        return config.MonitorPoints.Select(point =>
        {
            var isUp = health.GetIsUp(point.MonitorPointId);
            var status = health.GetStatus(point.MonitorPointId)
                ?? (isUp is null ? "Unknown" : isUp.Value ? "Healthy" : "Critical");
            return new MonitorPointStatusDto
            {
                MonitorPointId = point.MonitorPointId,
                DisplayName = point.DisplayName,
                Type = point.Type,
                Address = point.Address,
                Location = point.Location,
                Model = point.Model,
                Enabled = point.Enabled,
                ShowInShortcut = point.ShowInShortcut,
                IntervalSeconds = point.IntervalSeconds,
                Alert = point.Alert,
                IsUp = isUp,
                Status = point.Enabled ? status : "Unknown",
                LastCheckedUtc = health.GetLastCheckedUtc(point.MonitorPointId),
                Message = health.GetMessage(point.MonitorPointId),
                ResponseMs = health.GetResponseMs(point.MonitorPointId),
                StatusSinceUtc = health.GetStatusSinceUtc(point.MonitorPointId),
                DeviceKind = point.Type == MonitorPointType.Device ? point.DeviceKind : null,
                Target = MonitorPointText.Target(point)
            };
        }).ToList();
    }
}
