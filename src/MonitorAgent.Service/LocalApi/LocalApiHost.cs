using MonitorAgent.Service.Connectivity;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Platform;
using MonitorAgent.Service.Reports;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Constants;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Models.Reports;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace MonitorAgent.Service.LocalApi;

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

    /// <summary>Starts the API on the address and port from the settings, and restarts it when they change.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cache = _rootProvider.GetRequiredService<ILocalConfigCache>();
        while (!stoppingToken.IsCancellationRequested)
        {
            var listen = Listening.From(cache.GetGeneral(), _options.Port);
            if (!await TryStartAsync(listen, stoppingToken))
            {
                var fallback = listen with { Address = IPAddress.Loopback.ToString() };
                _logger.LogWarning("[API] Falling back to http://127.0.0.1:{Port}", fallback.Port);
                if (!await TryStartAsync(fallback, stoppingToken) && !await TryStartAsync(fallback with { Port = _options.Port }, stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
            }

            UpdateFirewall(listen);
            try
            {
                while (Listening.From(cache.GetGeneral(), _options.Port) == listen)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
            }

            await StopAppAsync();
            if (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[API] The listen address or port changed; restarting the API");
            }
        }
    }

    private async Task<bool> TryStartAsync(Listening listen, CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls(listen.Urls);
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

        var app = builder.Build();
        app.UseCors("localhost");
        app.Use(async (context, next) =>
        {
            var key = _rootProvider.GetRequiredService<ILocalConfigCache>().GetGeneral().RemoteAccessKey;
            var remote = context.Connection.RemoteIpAddress;
            if (!string.IsNullOrEmpty(key) && remote is not null && !IPAddress.IsLoopback(remote)
                && !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(context.Request.Headers[ApiRoutes.AccessKeyHeader].ToString()),
                    Encoding.UTF8.GetBytes(key)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("The access key is missing or wrong.");
                return;
            }

            await next();
        });
        app.Use(async (context, next) =>
        {
            var license = _rootProvider.GetRequiredService<ILicenseState>();
            if (!license.IsLicensed && !OpenWithoutLicense(context.Request.Path))
            {
                var status = license.GetStatus();
                await Results.Problem(
                    detail: status.Message,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "License required",
                    extensions: new Dictionary<string, object?> { ["code"] = LicenseCodes.Required, ["state"] = status.State.ToString() })
                    .ExecuteAsync(context);
                return;
            }

            await next();
        });
        MapEndpoints(app);

        try
        {
            await app.StartAsync(stoppingToken);
            _app = app;
            _listening = listen;
            _logger.LogInformation("[API] Listening on {Urls}", string.Join(", ", listen.Urls));
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException or FormatException)
        {
            _logger.LogError("[API] Could not listen on {Urls}: {Message}", string.Join(", ", listen.Urls), ex.Message);
            await app.DisposeAsync();
            return false;
        }
    }

    private async Task StopAppAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token);
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    /// <summary>Opens the port in the system firewall while the API listens beyond this computer, and closes it otherwise.</summary>
    private void UpdateFirewall(Listening listen)
    {
        var firewall = _rootProvider.GetRequiredService<IFirewall>();
        var allowed = firewall.Allow(listen.LocalOnly ? null : listen.Port);
        if (listen.LocalOnly)
        {
            return;
        }

        if (allowed)
        {
            _logger.LogInformation("[API] {Firewall} allows TCP port {Port}", firewall.Name, listen.Port);
        }
        else
        {
            _logger.LogWarning("[API] Could not open TCP port {Port} in the {Firewall}; other devices may not reach the API", listen.Port, firewall.Name);
        }
    }

    /// <param name="Address">127.0.0.1, 0.0.0.0 or one IP of this computer.</param>
    private sealed record Listening(string Address, int Port)
    {
        public bool LocalOnly => IPAddress.TryParse(Address, out var ip) && IPAddress.IsLoopback(ip);

        /// <summary>A single IP also listens on 127.0.0.1 so the app on this computer keeps working.</summary>
        public string[] Urls => LocalOnly
            ? [$"http://127.0.0.1:{Port}"]
            : Address == IPAddress.Any.ToString()
                ? [$"http://0.0.0.0:{Port}"]
                : [$"http://{Address}:{Port}", $"http://127.0.0.1:{Port}"];

        public static Listening From(GeneralRuntimeSettings general, int defaultPort)
        {
            var address = general.ServiceListenAddress?.Trim() ?? string.Empty;
            if (address is "" or "localhost" || !IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                address = IPAddress.Loopback.ToString();
            }

            var port = general.ServicePort is > 0 and <= 65535 ? general.ServicePort : defaultPort;
            return new Listening(address, port);
        }
    }

    private Listening? _listening;

    /// <summary>This computer's IPv4 addresses, for choosing the listen address in Settings.</summary>
    private static List<string> LocalAddresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToList();

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await StopAppAsync();
    }

    /// <summary>
    /// What the app may use while the service has no valid license: activating it and whether the service runs.
    /// Issues and notifications answer with the license notice only. Everything is still measured and saved meanwhile.
    /// </summary>
    private static bool OpenWithoutLicense(PathString path)
        => path.StartsWithSegments(ApiRoutes.License)
           || path.StartsWithSegments(ApiRoutes.Status)
           || path.StartsWithSegments(ApiRoutes.Issues)
           || path.StartsWithSegments(ApiRoutes.Notifications);

    private bool IsLicensed() => _rootProvider.GetRequiredService<ILicenseState>().IsLicensed;

    private AgentIssueDto LicenseNotice()
        => _rootProvider.GetRequiredService<IMonitorHealthStore>().GetIssues().FirstOrDefault(i => i.Id == LicenseCodes.IssueId)
           ?? new AgentIssueDto
           {
               Id = LicenseCodes.IssueId,
               Severity = "Critical",
               Title = "License required",
               Message = _rootProvider.GetRequiredService<ILicenseState>().GetStatus().Message
           };

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
                MachineName = cache.GetGeneral().DisplayName,
                Status = "Running",
                identity.Version,
                identity.Uptime,
                MadkhalConnected = connectivity.MadkhalAvailable,
                CentralConnected = connectivity.CentralAvailable,
                ConfigVersion = cache.GetConfigVersion(),
                LastSyncUtc = cache.GetLastSyncUtc(),
                ListenUrls = _listening?.Urls ?? [],
                Addresses = LocalAddresses()
            });
        });

        app.MapPost(ApiRoutes.DatabaseTest, async (DatabaseLogin login, CancellationToken ct) =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var failure = await DatabaseMonitor.ConnectFailureAsync(login, ct);
            _logger.LogInformation("[Database] Test from the app: {Server}/{Database} - {Result}", login.Server, login.Database, failure ?? "connected");
            return Results.Ok(new DatabaseTestResultDto(failure is null, failure ?? $"Connected in {watch.ElapsedMilliseconds} ms."));
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

        app.MapGet(ApiRoutes.Settings, () => Results.Ok(ServiceSettingsFile.ReadSection()));

        app.MapPut(ApiRoutes.Settings, (System.Text.Json.Nodes.JsonObject section) =>
        {
            try
            {
                var before = ServiceSettingsFile.ReadSection();
                ServiceSettingsFile.WriteSection(section);
                _logger.LogInformation("[Settings] Saved by the app to {Path}", ServiceSettingsFile.PrimaryPath);
                ReportSettingsChanges(SettingsChanges.Describe(before, section));
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(ex, "[Settings] Saving failed");
                return Results.Problem($"The service could not save its settings file: {ex.Message}");
            }
        });

        app.MapGet(ApiRoutes.DiskActivity, () =>
            _rootProvider.GetRequiredService<IDiskActivityService>().GetActivity() is { } activity
                ? Results.Ok(activity)
                : Results.NoContent());

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
            if (sort is not "cpu" and not "ram" and not "network" and not "disk")
            {
                return Results.BadRequest(new { error = "sortBy must be cpu, ram, network, or disk" });
            }

            var items = await _rootProvider.GetRequiredService<ISystemInfoService>()
                .GetTopProcessesSortedAsync(count ?? 10, sort, ct);
            return Results.Ok(items);
        });

        app.MapGet(ApiRoutes.Programs, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetProgramsAsync(ct)));

        app.MapGet(ApiRoutes.Users, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetUsersAsync(ct)));

        app.MapGet(ApiRoutes.Services, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetServicesAsync(ct)));

        app.MapGet(ApiRoutes.MonitorPoints, async (CancellationToken ct) =>
            Results.Ok(await MonitorPointStatusBuilder.BuildAsync(_rootProvider, ct)));

        app.MapGet(ApiRoutes.Issues, () =>
        {
            var issues = _rootProvider.GetRequiredService<IMonitorHealthStore>().GetIssues();
            return Results.Ok(IsLicensed() ? issues : issues.Where(i => i.Id == LicenseCodes.IssueId).ToList());
        });

        app.MapGet(ApiRoutes.Notifications, () => IsLicensed()
            ? Results.Ok(_rootProvider.GetRequiredService<INotificationStore>().GetNotifications())
            : Results.Ok(new[] { LicenseNotice() }));

        app.MapGet(ApiRoutes.Internet, () =>
            Results.Ok(_rootProvider.GetRequiredService<IInternetStatus>().GetState()));

        app.MapGet(ApiRoutes.ReportSubjects, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ReportBuilder>().GetSubjectsAsync(ct)));

        app.MapGet($"{ApiRoutes.Reports}/{{type}}", async (string type, DateTime? from, DateTime? to, string? subject, CancellationToken ct) =>
        {
            if (ReportTypes.Find(type) is null)
            {
                return Results.NotFound($"Unknown report \"{type}\".");
            }

            var end = to ?? DateTime.Now;
            var start = from ?? end.AddDays(-1);
            if (start >= end)
            {
                return Results.BadRequest("The start of the period must be before its end.");
            }

            _logger.LogInformation("[Reports] Building {Type}{Subject} for {From:yyyy-MM-dd HH:mm} - {To:yyyy-MM-dd HH:mm}",
                type, subject is null ? string.Empty : $" ({subject})", start, end);
            try
            {
                return Results.Ok(await _rootProvider.GetRequiredService<ReportBuilder>().BuildAsync(type, start, end, subject, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(ex.Message);
            }
        });

        app.MapPost(ApiRoutes.InternetSpeedTest, () =>
        {
            var internet = _rootProvider.GetRequiredService<IInternetStatus>();
            internet.StartSpeedTest();
            return Results.Accepted(ApiRoutes.Internet, internet.GetState());
        });

        app.MapGet(ApiRoutes.License, () => Results.Ok(_rootProvider.GetRequiredService<ILicenseState>().GetStatus()));

        app.MapPost(ApiRoutes.LicenseActivate, async (LicenseActivateRequest request, CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ILicenseState>().ActivateAsync(request.ProductKey, ct)));

        app.MapPost(ApiRoutes.LicenseDeactivate, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ILicenseState>().DeactivateAsync(ct)));

        app.MapPost(ApiRoutes.LicenseRefresh, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ILicenseState>().RefreshAsync(ct)));
    }

    /// <summary>Saves the settings changes to the Data file and shows them as a green notification for a few seconds.</summary>
    private void ReportSettingsChanges(List<string> changes)
    {
        if (changes.Count == 0)
        {
            _logger.LogInformation("[Settings] Nothing changed");
            return;
        }

        foreach (var change in changes)
        {
            _logger.LogInformation("[Settings] Changed - {Change}", change);
        }

        var now = DateTime.UtcNow;
        _rootProvider.GetRequiredService<ReportStore>().AddSettingsChanges(now, changes);
        _rootProvider.GetRequiredService<IIssueDataLogger>().Record("Settings changed", [new AgentIssueDto
        {
            Id = "settings:changed",
            Severity = "Changed",
            Title = $"Settings changed ({changes.Count})",
            Message = string.Join("; ", changes),
            TimestampUtc = now
        }]);

        const int shown = 5;
        var message = string.Join("; ", changes.Take(shown))
            + (changes.Count > shown ? $"; and {changes.Count - shown} more." : ".");
        _rootProvider.GetRequiredService<INotificationStore>().ShowTemporary(new AgentIssueDto
        {
            Id = $"settings:changed:{now.Ticks}",
            Severity = "Success",
            Title = changes.Count == 1 ? "Settings saved: 1 change" : $"Settings saved: {changes.Count} changes",
            Message = message,
            TimestampUtc = now
        }, TimeSpan.FromSeconds(10));
        _rootProvider.GetRequiredService<NotificationTrigger>().Request();
    }
}
