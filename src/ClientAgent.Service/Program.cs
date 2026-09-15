using ClientAgent.Service.Config;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Dispatch;
using ClientAgent.Service.LocalApi;
using ClientAgent.Service.Messaging;
using ClientAgent.Service.Monitoring;
using ClientAgent.Service.Options;
using ClientAgent.Service.Outbox;
using ClientAgent.Service.Runtime;
using ClientAgent.Service.SystemInfo;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(@"C:\ProgramData\ClientAgent\logs\bootstrap-.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "ClientAgentService";
    });
    builder.Services.AddSerilog((services, configuration) =>
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.File(
                builder.Configuration["Serilog:WriteTo:0:Args:path"] ?? @"C:\ProgramData\ClientAgent\logs\agent-.log",
                rollingInterval: RollingInterval.Day));

    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
    builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
    builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.SectionName));
    builder.Services.Configure<LocalApiOptions>(builder.Configuration.GetSection(LocalApiOptions.SectionName));
    builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));

    builder.Services.AddSingleton<IAgentIdentity, AgentIdentity>();
    builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();
    builder.Services.AddSingleton<HardwareMonitorReader>();
    builder.Services.AddSingleton<ISystemInfoService, SystemInfoService>();
    builder.Services.AddSingleton<IMonitoringEventBus, MonitoringEventBus>();
    builder.Services.AddSingleton<IEventPublisher, EventPublisher>();
    builder.Services.AddSingleton<IConnectivityTracker, ConnectivityTracker>();
    builder.Services.AddSingleton<IRoutingService, RoutingService>();
    builder.Services.AddSingleton<ILocalConfigCache, LocalConfigCache>();
    builder.Services.AddSingleton<EventDispatcher>();
    builder.Services.AddHttpClient("madkhal");
    builder.Services.AddHttpClient("central");
    builder.Services.AddHttpClient<IEventSender, EventSender>();

    builder.Services.AddHostedService<ResourceMonitor>();
    builder.Services.AddHostedService<DeviceMonitor>();
    builder.Services.AddHostedService<DatabaseMonitor>();
    builder.Services.AddHostedService<MadkhalMonitor>();
    builder.Services.AddHostedService<HeartbeatMonitor>();
    builder.Services.AddHostedService<DispatcherWorker>();
    builder.Services.AddHostedService<ConfigPuller>();
    builder.Services.AddHostedService<LocalApiHost>();

    var host = builder.Build();
    await host.Services.GetRequiredService<IOutboxRepository>().InitializeDatabaseAsync();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Client agent terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
