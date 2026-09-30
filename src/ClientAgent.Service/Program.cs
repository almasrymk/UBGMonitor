using ClientAgent.Service.Config;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.LocalApi;
using ClientAgent.Service.Monitoring;
using ClientAgent.Service.Options;
using ClientAgent.Service.Reports;
using ClientAgent.Service.Runtime;
using ClientAgent.Service.SystemInfo;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(@"C:\ProgramData\ClientAgent\logs\bootstrap-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: null)
    .CreateBootstrapLogger();

try
{
    // A Windows service starts in System32, so appsettings.json must be resolved from the exe folder.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    Log.Information("Settings file: {Path}", Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "ClientAgentService";
    });
    builder.Services.AddSerilog((services, configuration) =>
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                DataCleaner.LogPath(builder.Configuration),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 50L * 1024 * 1024));

    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
    builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.SectionName));
    builder.Services.Configure<LocalApiOptions>(builder.Configuration.GetSection(LocalApiOptions.SectionName));
    builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));

    builder.Services.AddSingleton<IAgentIdentity, AgentIdentity>();
    builder.Services.AddSingleton<HardwareMonitorReader>();
    builder.Services.AddSingleton<IHardwareService, HardwareService>();
    builder.Services.AddSingleton<ISensorsService, SensorsService>();
    builder.Services.AddSingleton<NetworkService>();
    builder.Services.AddSingleton<INetworkService>(sp => sp.GetRequiredService<NetworkService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<NetworkService>());
    builder.Services.AddSingleton<ISystemInfoService, SystemInfoService>();
    builder.Services.AddSingleton<DiskActivityService>();
    builder.Services.AddSingleton<IDiskActivityService>(sp => sp.GetRequiredService<DiskActivityService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DiskActivityService>());
    builder.Services.AddSingleton<IConnectivityTracker, ConnectivityTracker>();
    builder.Services.AddSingleton<IMonitorHealthStore, MonitorHealthStore>();
    builder.Services.AddSingleton<ILocalConfigCache, LocalConfigCache>();
    builder.Services.AddSingleton<NotificationTrigger>();
    builder.Services.AddSingleton<ReportStore>();
    builder.Services.AddSingleton<ReportBuilder>();
    builder.Services.AddHostedService<MetricsRecorder>();
    builder.Services.AddHostedService<DataCleaner>();
    builder.Services.AddSingleton<IssueDataLogger>();
    builder.Services.AddSingleton<IIssueDataLogger>(sp => sp.GetRequiredService<IssueDataLogger>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<IssueDataLogger>());
    builder.Services.AddSingleton<InternetMonitor>();
    builder.Services.AddSingleton<IInternetStatus>(sp => sp.GetRequiredService<InternetMonitor>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<InternetMonitor>());
    builder.Services.AddSingleton<IConnectivityFilter, ConnectivityFilter>();
    builder.Services.AddSingleton<NotificationEngine>();
    builder.Services.AddSingleton<INotificationStore>(sp => sp.GetRequiredService<NotificationEngine>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationEngine>());
    builder.Services.AddHttpClient("madkhal");
    builder.Services.AddHttpClient("central");
    builder.Services.AddHttpClient("website", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    });

    builder.Services.AddHostedService<ResourceMonitor>();
    builder.Services.AddHostedService<DeviceMonitor>();
    builder.Services.AddHostedService<ApplicationMonitor>();
    builder.Services.AddHostedService<WebsiteMonitor>();
    builder.Services.AddHostedService<DatabaseMonitor>();
    builder.Services.AddHostedService<MadkhalMonitor>();
    builder.Services.AddHostedService<ConfigPuller>();
    builder.Services.AddHostedService<LocalApiHost>();

    var host = builder.Build();
    _ = host.Services.GetRequiredService<ISensorsService>();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Client agent terminated unexpectedly");
    // A non-zero exit code makes the Windows service recovery actions restart the agent.
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
