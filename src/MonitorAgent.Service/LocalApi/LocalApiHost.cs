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
using MonitorAgent.Shared.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;

namespace MonitorAgent.Service.LocalApi;

public sealed class LocalApiHost : BackgroundService
{
    private readonly IServiceProvider _rootProvider;
    private readonly LocalApiOptions _options;
    private readonly ILogger<LocalApiHost> _logger;
    private WebApplication? _app;
    private X509Certificate2? _remoteCertificate;
    private readonly RemoteAbuseGuard _abuse = new();

    public LocalApiHost(IServiceProvider rootProvider, IOptions<LocalApiOptions> options, ILogger<LocalApiHost> logger)
    {
        _rootProvider = rootProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Starts the API on the address and port from the settings, and restarts it when they change.</summary>
    private readonly List<WebApplication> _localApps = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var role in new[] { AgentAccessRole.Viewer, AgentAccessRole.Administrator })
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            ConfigureServices(builder.Services);
            IpcHostConfiguration.Configure(builder, role, _logger);
            var local = builder.Build();
            ConfigureApplication(local, role);
            try { await local.StartAsync(stoppingToken); IpcHostConfiguration.Finish(role, _logger); _localApps.Add(local); }
            catch { await local.DisposeAsync(); throw; }
        }
        var cache = _rootProvider.GetRequiredService<ILocalConfigCache>();
        var remote = _rootProvider.GetRequiredService<RemoteAccessManager>();
        var legacy = cache.GetGeneral();
        try
        {
        remote.MigrateLegacy(legacy.RemoteAccessKey, !Listening.From(legacy, _options.Port).LocalOnly);
        if (!string.IsNullOrEmpty(legacy.RemoteAccessKey))
        {
            var settings = ServiceSettingsFile.ReadSection();
            if (settings["General"] is System.Text.Json.Nodes.JsonObject oldGeneral)
            {
                PrivateFile.Backup(ServiceSettingsFile.PrimaryPath, Path.Combine(AgentPaths.StateFolder, "backups"));
                oldGeneral["RemoteAccessKey"] = "";
                ServiceSettingsFile.WriteSection(settings);
            }
        }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or System.Text.Json.JsonException)
        {
            _logger.LogError("[API] Remote state migration failed; local administration remains available. Error type: {ErrorType}", ex.GetType().Name);
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var general = cache.GetGeneral();
            var listen = Listening.From(general, _options.Port);
            string revision;
            RemoteAccessStatus remoteStatus;
            try { revision = remote.Revision; remoteStatus = remote.Status(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or System.Text.Json.JsonException)
            {
                await StopAppAsync();
                _rootProvider.GetRequiredService<IFirewall>().Allow(null);
                _logger.LogError("[API] Remote security state cannot be loaded. Local administration remains available. Error type: {ErrorType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }
            if (!listen.LocalOnly && remoteStatus.Enabled && remoteStatus.HasViewerKey)
            {
                if (!await TryStartAsync(listen, stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                if (remoteStatus.OpenFirewall) UpdateFirewall(listen);
                else _rootProvider.GetRequiredService<IFirewall>().Allow(null);
            }
            else _rootProvider.GetRequiredService<IFirewall>().Allow(null);
            try
            {
                while (Listening.From(cache.GetGeneral(), _options.Port) == listen && remote.Revision == revision)
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("[API] Remote state changed but cannot be read: {ErrorType}", ex.GetType().Name);
            }
            await StopAppAsync();
        }
    }
    private async Task<bool> TryStartAsync(Listening listen, CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        X509Certificate2 certificate;
        try { certificate = _rootProvider.GetRequiredService<RemoteAccessManager>().GetCertificate(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or System.Text.Json.JsonException)
        {
            _rootProvider.GetRequiredService<IFirewall>().Allow(null);
            _logger.LogError("[API] Remote certificate could not be loaded: {ErrorType}", ex.GetType().Name);
            return false;
        }
        if (certificate.NotAfter <= DateTime.Now) { certificate.Dispose(); _logger.LogError("[API] Remote certificate expired; regenerate it explicitly from the local Administrator connection."); return false; }
        RemoteTransport.Configure(builder, IPAddress.Parse(listen.Address), listen.Port, certificate);
        ConfigureServices(builder.Services, tcp: true);
        var app = builder.Build();
        ConfigureApplication(app, AgentAccessRole.Viewer, tcp: true, boundAddress: listen.Address);

        try
        {
            await app.StartAsync(stoppingToken);
            _app = app;
            _remoteCertificate = certificate;
            _listening = listen;
            _logger.LogInformation("[API] Listening on {Urls}", string.Join(", ", listen.Urls));
            _logger.LogInformation("[API] Certificate SHA256 fingerprint: {Fingerprint}", certificate.GetCertHashString(HashAlgorithmName.SHA256));
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException or FormatException)
        {
            _logger.LogError("[API] Could not listen on {Urls}: {Message}", string.Join(", ", listen.Urls), ex.Message);
            await app.DisposeAsync();
            certificate.Dispose();
            return false;
        }
    }

    public static void ConfigureServices(IServiceCollection services, bool tcp = false)
    {
        if (!tcp) return;
        services.AddRequestTimeouts(o => o.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(30) });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public void ConfigureApplication(WebApplication app, AgentAccessRole transportRole = AgentAccessRole.None, bool tcp = false, string? boundAddress = null)
    {
        app.UseRouting();
        if (tcp) { app.UseRequestTimeouts(); app.UseRateLimiter(); }
        app.Use(async (context, next) =>
        {
            var actualRole = transportRole;
            if (tcp)
            {
                if (context.Request.ContentLength > 8 * 1024 * 1024) { context.Response.StatusCode = 413; return; }
                var peer = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (_abuse.IsBlocked(peer)) { context.Response.StatusCode = 429; return; }
                actualRole = _rootProvider.GetRequiredService<RemoteAccessManager>().Authenticate(context.Request.Headers[ApiRoutes.AccessKeyHeader].ToString());
                if (actualRole == AgentAccessRole.None)
                {
                    _abuse.Failed(peer);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                var host = context.Request.Host.Host;
                var computer = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                var names = new[] { Environment.MachineName, computer.HostName, computer.HostName + "." + computer.DomainName };
                var allowed = (boundAddress == IPAddress.Any.ToString() ? LocalAddresses().Append("127.0.0.1").Contains(host) : host == boundAddress)
                    || names.Contains(host, StringComparer.OrdinalIgnoreCase);
                if (!allowed) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
            }
            var required = context.GetEndpoint()?.Metadata.GetMetadata<RequiredAgentRole>();
            if (required is null || actualRole < required.Role || tcp && context.GetEndpoint()?.Metadata.GetMetadata<LocalAdministrationOnly>() is not null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            var failed = false;
            context.Items["AgentRole"] = actualRole;
            try { await next(); }
            catch { failed = true; throw; }
            finally
            {
                if (required.Role == AgentAccessRole.Administrator)
                    _logger.LogInformation("[Audit] Role={Role} Transport={Transport} Action={Action} Result={Result}", actualRole, tcp ? "TCP" : "IPC", context.GetEndpoint()?.DisplayName, failed ? 500 : context.Response.StatusCode);
            }
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
        MapEndpoints(app, tcp);

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
            _remoteCertificate?.Dispose(); _remoteCertificate = null;
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
            ? [$"https://127.0.0.1:{Port}"]
            : Address == IPAddress.Any.ToString()
                ? [$"https://0.0.0.0:{Port}"]
                : [$"https://{Address}:{Port}"];

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
        foreach (var local in _localApps) { await local.StopAsync(cancellationToken); await local.DisposeAsync(); }
        _localApps.Clear();
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

    private void MapEndpoints(WebApplication app, bool tcp)
    {
        var viewer = app.MapGroup("").WithMetadata(new RequiredAgentRole(AgentAccessRole.Viewer));
        var admin = app.MapGroup("").WithMetadata(new RequiredAgentRole(AgentAccessRole.Administrator));
        admin.MapGet(ApiRoutes.RemoteAccess, () => Results.Ok(_rootProvider.GetRequiredService<RemoteAccessManager>().Status())).WithMetadata(new LocalAdministrationOnly());
        admin.MapPost(ApiRoutes.RemoteAccess, (RemoteAccessAction action, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(_rootProvider.GetRequiredService<RemoteAccessManager>().Apply(action)); }
            catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return Results.BadRequest(new { error = "Remote action failed. Check required keys, certificate and selected options." }); }
        }).WithMetadata(new LocalAdministrationOnly());
        viewer.MapGet(ApiRoutes.Status, (HttpContext context) =>
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
                Addresses = LocalAddresses(),
                AccessRole = context.Items["AgentRole"]?.ToString()
            });
        });

        admin.MapPost(ApiRoutes.DatabaseTest, async (DatabaseTestRequest request, CancellationToken ct) =>
        {
            DatabaseLogin login;
            try { login = DatabaseTestResolver.Resolve(request, ServiceSettingsFile.ReadSection()); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var failure = await DatabaseMonitor.ConnectFailureAsync(login, ct);
            _logger.LogInformation("[Database] Administrator connection test: {Success}", failure is null);
            return Results.Ok(new DatabaseTestResultDto(failure is null, failure is null ? $"Connected in {watch.ElapsedMilliseconds} ms." : "Connection failed. Check the target, credentials and certificate configuration."));
        });
        admin.MapPost(ApiRoutes.PasswordReveal, (PasswordRevealRequest request, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var login = DatabaseTestResolver.Resolve(new(request.MonitorPointId), ServiceSettingsFile.ReadSection());
                if (login.IntegratedSecurity) return Results.BadRequest(new { error = "Integrated authentication has no saved password." });
                var password = SecretProtector.Unprotect(login.Password);
                return password.Length == 0 ? Results.NotFound() : Results.Ok(new PasswordRevealResult(password));
            }
            catch (ArgumentException) { return Results.NotFound(); }
        });

        viewer.MapGet(ApiRoutes.Snapshot, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetSnapshotAsync(ct)));

        viewer.MapGet(ApiRoutes.Cpu, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetCpuAsync(ct)));

        viewer.MapGet(ApiRoutes.Ram, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetRamAsync(ct)));

        viewer.MapGet(ApiRoutes.Network, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetNetworkAsync(ct)));

        viewer.MapGet(ApiRoutes.DiskPartitions, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetPartitionsAsync(ct)));

        viewer.MapGet(ApiRoutes.DiskPhysical, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISystemInfoService>().GetPhysicalDisksAsync(ct)));

        viewer.MapGet(ApiRoutes.Settings, () => Results.Ok(SettingsContract.PublicSettings(ServiceSettingsFile.ReadSection())));

        admin.MapPut(ApiRoutes.Settings, (System.Text.Json.Nodes.JsonObject section) =>
        {
            try
            {
                var before = ServiceSettingsFile.ReadSection();
                section = SettingsContract.Merge(section, before);
                if (tcp && (!System.Text.Json.Nodes.JsonNode.DeepEquals(section["General"]?["ServiceListenAddress"], before["General"]?["ServiceListenAddress"])
                    || !System.Text.Json.Nodes.JsonNode.DeepEquals(section["General"]?["ServicePort"], before["General"]?["ServicePort"])))
                    return Results.BadRequest(new { error = "Network listener settings can only be changed through local administration." });
                ServiceSettingsFile.WriteSection(section);
                _logger.LogInformation("[Settings] Saved by the app to {Path}", ServiceSettingsFile.PrimaryPath);
                ReportSettingsChanges(SettingsChanges.Describe(before, section));
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex is ArgumentException ? ex.Message : "Invalid settings data." });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("[Settings] Saving failed: {ErrorType}", ex.GetType().Name);
                return Results.Problem("The service could not save its settings file. Check service permissions and available disk space.");
            }
        });

        viewer.MapGet(ApiRoutes.DiskActivity, () =>
            _rootProvider.GetRequiredService<IDiskActivityService>().GetActivity() is { } activity
                ? Results.Ok(activity)
                : Results.NoContent());

        viewer.MapGet(ApiRoutes.Hardware, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetHardwareAsync(ct)));

        viewer.MapGet(ApiRoutes.HardwareLevels, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetStaticLevelsAsync(ct)));

        viewer.MapGet($"{ApiRoutes.HardwareLevels}/{{level:int}}", async (int level, CancellationToken ct) =>
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

        viewer.MapGet(ApiRoutes.Os, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IHardwareService>().GetOsAsync(ct)));

        viewer.MapGet(ApiRoutes.Sensors, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ISensorsService>().GetSensorsAsync(ct)));

        viewer.MapGet(ApiRoutes.ProcessesTop, async (int? count, string? sortBy, CancellationToken ct) =>
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

        viewer.MapGet(ApiRoutes.Programs, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetProgramsAsync(ct)));

        viewer.MapGet(ApiRoutes.Users, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetUsersAsync(ct)));

        viewer.MapGet(ApiRoutes.Services, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<IApplicationsService>().GetServicesAsync(ct)));

        viewer.MapGet(ApiRoutes.MonitorPoints, async (CancellationToken ct) =>
            Results.Ok(await MonitorPointStatusBuilder.BuildAsync(_rootProvider, ct)));

        viewer.MapGet(ApiRoutes.Issues, () =>
        {
            var issues = _rootProvider.GetRequiredService<IMonitorHealthStore>().GetIssues();
            return Results.Ok(IsLicensed() ? issues : issues.Where(i => i.Id == LicenseCodes.IssueId).ToList());
        });

        viewer.MapGet(ApiRoutes.Notifications, () => IsLicensed()
            ? Results.Ok(_rootProvider.GetRequiredService<INotificationStore>().GetNotifications())
            : Results.Ok(new[] { LicenseNotice() }));

        viewer.MapGet(ApiRoutes.Internet, () =>
            Results.Ok(_rootProvider.GetRequiredService<IInternetStatus>().GetState()));

        viewer.MapGet(ApiRoutes.ReportSubjects, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ReportBuilder>().GetSubjectsAsync(ct)));

        viewer.MapGet($"{ApiRoutes.Reports}/{{type}}", async (string type, DateTime? from, DateTime? to, string? subject, CancellationToken ct) =>
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

        admin.MapPost(ApiRoutes.InternetSpeedTest, () =>
        {
            var internet = _rootProvider.GetRequiredService<IInternetStatus>();
            internet.StartSpeedTest();
            return Results.Accepted(ApiRoutes.Internet, internet.GetState());
        });

        viewer.MapGet(ApiRoutes.License, () => Results.Ok(_rootProvider.GetRequiredService<ILicenseState>().GetStatus()));

        admin.MapPost(ApiRoutes.LicenseActivate, async (LicenseActivateRequest request, CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ILicenseState>().ActivateAsync(request.ProductKey, ct)));

        admin.MapPost(ApiRoutes.LicenseDeactivate, async (CancellationToken ct) =>
            Results.Ok(await _rootProvider.GetRequiredService<ILicenseState>().DeactivateAsync(ct)));

        admin.MapPost(ApiRoutes.LicenseRefresh, async (CancellationToken ct) =>
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
