using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Config;
using ClientAgent.Service.Monitoring;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Service.SystemInfo;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.LocalApi;

public sealed class LocalApiHost : BackgroundService
{
    private readonly IServiceProvider _rootProvider;
    private readonly LocalApiOptions _options;
    private readonly ILogger<LocalApiHost> _logger;
    private WebApplication? _app;

    public LocalApiHost(IServiceProvider rootProvider, IOptions<LocalApiOptions> options, ILogger<LocalApiHost> logger)
    {
        _rootProvider = rootProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_options.Port}");
        builder.Services.AddCors(o => o.AddPolicy("localhost", p =>
            p.SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    return uri.Host is "localhost" or "127.0.0.1";
                })
                .AllowAnyHeader()
                .AllowAnyMethod()));

        _app = builder.Build();
        _app.UseCors("localhost");
        MapEndpoints(_app);

        _logger.LogInformation("Local API listening on http://127.0.0.1:{Port}", _options.Port);
        await _app.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet(ApiRoutes.Status, () =>
        {
            var identity = _rootProvider.GetRequiredService<IAgentIdentity>();
            var connectivity = _rootProvider.GetRequiredService<IConnectivityTracker>();
            var cache = _rootProvider.GetRequiredService<ILocalConfigCache>();
            return Results.Ok(new
            {
                identity.AgentId,
                Status = "Running",
                identity.Version,
                identity.Uptime,
                MadkhalConnected = connectivity.MadkhalAvailable,
                CentralConnected = connectivity.CentralAvailable,
                ConfigVersion = cache.GetConfigVersion(),
                LastSyncUtc = cache.GetLastSyncUtc()
            });
        });

        app.MapGet(ApiRoutes.Snapshot, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetSnapshotAsync(ct)));

        app.MapGet(ApiRoutes.Cpu, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetCpuAsync(ct)));

        app.MapGet(ApiRoutes.Ram, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetRamAsync(ct)));

        app.MapGet(ApiRoutes.Network, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetNetworkAsync(ct)));

        app.MapGet(ApiRoutes.DiskPartitions, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetPartitionsAsync(ct)));

        app.MapGet(ApiRoutes.DiskPhysical, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetPhysicalDisksAsync(ct)));

        app.MapGet(ApiRoutes.Hardware, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetHardwareAsync(ct)));

        app.MapGet(ApiRoutes.HardwareLevels, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetStaticLevelsAsync(ct)));

        app.MapGet($"{ApiRoutes.HardwareLevels}/{{level:int}}", async (int level, CancellationToken ct) =>
        {
            HardwareLevelDto? dto = level switch
            {
                1 or 2 or 4 => await _rootProvider.GetRequiredService<IHardwareService>().GetLevelAsync(level, ct),
                3 => await _rootProvider.GetRequiredService<ISensorsService>().GetLevelAsync(ct),
                5 => await _rootProvider.GetRequiredService<INetworkService>().GetLevelAsync(ct),
                _ => null
            };

            return dto is null ? Results.BadRequest(new { error = "level must be 1-5" }) : Results.Ok(dto);
        });

        app.MapGet(ApiRoutes.Os, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetOsAsync(ct)));

        app.MapGet(ApiRoutes.Sensors, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISensorsService>().GetSensorsAsync(ct)));

        app.MapGet(ApiRoutes.ProcessesTop, async (int? count, string? sortBy, CancellationToken ct) =>
        {
            var sort = string.IsNullOrWhiteSpace(sortBy) ? "cpu" : sortBy.Trim().ToLowerInvariant();
            if (sort is not "cpu" and not "ram" and not "network")
            {
                return Results.BadRequest(new { error = "sortBy must be cpu, ram, or network" });
            }

            var items = await _rootProvider.GetRequiredService<ISystemInfoService>()
                .GetTopProcessesSortedAsync(count ?? 10, sort, ct);
            return Results.Ok(items);
        });

        app.MapGet(ApiRoutes.MonitorPoints, async (CancellationToken ct) =>
        {
            var config = await _rootProvider.GetRequiredService<ILocalConfigCache>().GetConfigAsync(ct);
            var health = _rootProvider.GetRequiredService<IMonitorHealthStore>();
            var items = config.MonitorPoints.Select(point =>
            {
                var isUp = health.GetIsUp(point.MonitorPointId);
                var status = isUp is null ? "Unknown" : isUp.Value ? "Healthy" : "Critical";
                return new MonitorPointStatusDto
                {
                    MonitorPointId = point.MonitorPointId,
                    DisplayName = point.DisplayName,
                    Type = point.Type,
                    Address = point.Address,
                    Location = point.Location,
                    Model = point.Model,
                    Enabled = point.Enabled,
                    IntervalSeconds = point.IntervalSeconds,
                    IsUp = isUp,
                    Status = point.Enabled ? status : "Unknown",
                    LastCheckedUtc = health.GetLastCheckedUtc(point.MonitorPointId),
                    Message = health.GetMessage(point.MonitorPointId)
                };
            }).ToList();
            return Results.Ok(items);
        });

        app.MapGet(ApiRoutes.Issues, () =>
            Results.Ok(_rootProvider.GetRequiredService<IMonitorHealthStore>().GetIssues()));
    }
}
