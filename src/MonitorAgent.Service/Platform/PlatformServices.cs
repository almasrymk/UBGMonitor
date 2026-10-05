using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using MonitorAgent.Service.Platform.Linux;
using MonitorAgent.Service.Platform.Mac;
using MonitorAgent.Service.Platform.Windows;
using MonitorAgent.Service.SystemInfo;

namespace MonitorAgent.Service.Platform;

public static class PlatformServices
{
    /// <summary>Registers the readers for the system the agent is running on.</summary>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            AddWindows(services);
        }
        else if (OperatingSystem.IsLinux())
        {
            AddLinux(services);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddMac(services);
        }
        else
        {
            services.AddSingleton<ISystemProbe, BasicSystemProbe>();
            services.AddSingleton<IInventoryCollector, BasicInventoryCollector>();
            services.AddSingleton<ISensorReader, NoSensorReader>();
            services.AddSingleton<IDiskActivityReader, NoDiskActivityReader>();
            services.AddSingleton<IFirewall, NoFirewall>();
            services.AddSingleton<IHostInventory, BasicHostInventory>();
        }

        return services;
    }

    // Kept out of the method above so other systems never load the Windows-only libraries (LibreHardwareMonitor).
    [SupportedOSPlatform("windows")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddWindows(IServiceCollection services)
    {
        services.AddSingleton<ISystemProbe, WindowsSystemProbe>();
        services.AddSingleton<IInventoryCollector, WindowsInventoryCollector>();
        services.AddSingleton<ISensorReader, HardwareMonitorReader>();
        services.AddSingleton<IDiskActivityReader, WindowsDiskActivityReader>();
        services.AddSingleton<IFirewall, WindowsFirewall>();
        services.AddSingleton<IHostInventory, WindowsHostInventory>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddLinux(IServiceCollection services)
    {
        services.AddSingleton<ISystemProbe, LinuxSystemProbe>();
        services.AddSingleton<IInventoryCollector, LinuxInventoryCollector>();
        services.AddSingleton<ISensorReader, LinuxSensorReader>();
        services.AddSingleton<IDiskActivityReader, LinuxDiskActivityReader>();
        services.AddSingleton<IFirewall, LinuxFirewall>();
        services.AddSingleton<IHostInventory, LinuxHostInventory>();
    }

    [SupportedOSPlatform("macos")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddMac(IServiceCollection services)
    {
        services.AddSingleton<ISystemProbe, MacSystemProbe>();
        services.AddSingleton<IInventoryCollector, MacInventoryCollector>();
        services.AddSingleton<ISensorReader, MacSensorReader>();
        services.AddSingleton<IDiskActivityReader, MacDiskActivityReader>();
        services.AddSingleton<IFirewall, MacFirewall>();
        services.AddSingleton<IHostInventory, MacHostInventory>();
    }
}
