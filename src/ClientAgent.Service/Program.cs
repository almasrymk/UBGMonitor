using ClientAgent.Service.Config;
using ClientAgent.Service.Connectivity;
using ClientAgent.Service.LocalApi;
using ClientAgent.Service.Monitoring;
using ClientAgent.Service.Options;
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
    builder.Services.AddSingleton<IConnectivityTracker, ConnectivityTracker>();
    builder.Services.AddSingleton<IMonitorHealthStore, MonitorHealthStore>();
    builder.Services.AddSingleton<ILocalConfigCache, LocalConfigCache>();
    builder.Services.AddHttpClient("madkhal");
    builder.Services.AddHttpClient("central");

    builder.Services.AddHostedService<ResourceMonitor>();
    builder.Services.AddHostedService<DeviceMonitor>();
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
}
finally
{
    await Log.CloseAndFlushAsync();
}
