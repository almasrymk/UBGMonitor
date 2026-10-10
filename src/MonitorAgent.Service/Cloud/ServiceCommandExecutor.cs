using System.Diagnostics;
using System.ServiceProcess;
using MonitorAgent.Cloud;
using MonitorAgent.Service.Monitoring;

namespace MonitorAgent.Service.Cloud;

/// <summary>
/// Runs the verified remote actions of AG-13 (M11). <c>refresh-inventory</c> is handled by the connector. A speed test
/// starts through the internet monitor; <c>restart-agent</c> exits with code 1 a few seconds later so the service
/// manager (Windows recovery actions, systemd <c>Restart=on-failure</c>, launchd <c>KeepAlive</c>) starts it again,
/// after the result has left the outbox. Service actions use the OS service manager with the name as one argument,
/// never through a shell; the agent's own service is changed only with <c>restart-agent</c>.
/// </summary>
public sealed class ServiceCommandExecutor(IInternetStatus internet, IHostApplicationLifetime lifetime, ILogger<ServiceCommandExecutor> logger) : ICloudCommandExecutor
{
    public static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] OwnServices = ["MonitorAgent", "monitoragent", "com.ubg.monitoragent"];

    public async Task<(bool Success, string Output)> ExecuteAsync(string type, string? service, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case "run-speed-test":
                internet.StartSpeedTest();
                return (true, "Speed test started; the result arrives with the next snapshot.");
            case "restart-agent":
                logger.LogWarning("[Cloud] Restarting the agent on a remote action");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(RestartDelay, CancellationToken.None);
                    Environment.ExitCode = 1;
                    lifetime.StopApplication();
                }, CancellationToken.None);
                return (true, $"The agent restarts in {RestartDelay.TotalSeconds:0} seconds.");
            case "service-start" or "service-stop" or "service-restart" when service is not null:
                if (OwnServices.Contains(service, StringComparer.OrdinalIgnoreCase))
                    return (false, "The agent's own service is restarted with restart-agent only.");
                return await ServiceAsync(type["service-".Length..], service, cancellationToken);
            default:
                return (false, $"'{type}' is not supported by this agent.");
        }
    }

    private static async Task<(bool, string)> ServiceAsync(string action, string name, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return await WindowsServiceAsync(action, name, cancellationToken);
        if (OperatingSystem.IsLinux())
            return await RunAsync("systemctl", [action, name], cancellationToken);
        if (OperatingSystem.IsMacOS())
        {
            string[] arguments = action switch
            {
                "start" => ["kickstart", $"system/{name}"],
                "restart" => ["kickstart", "-k", $"system/{name}"],
                _ => ["kill", "SIGTERM", $"system/{name}"],
            };
            return await RunAsync("launchctl", arguments, cancellationToken);
        }

        return (false, "Service actions are not supported on this operating system.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<(bool, string)> WindowsServiceAsync(string action, string name, CancellationToken cancellationToken)
    {
        try
        {
            using var controller = new ServiceController(name);
            if (action is "stop" or "restart" && controller.Status != ServiceControllerStatus.Stopped)
            {
                controller.Stop();
                await Task.Run(() => controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout), cancellationToken);
            }

            if (action is "start" or "restart")
            {
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.Start();
                    await Task.Run(() => controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout), cancellationToken);
                }
            }

            controller.Refresh();
            return (true, $"{name}: {controller.Status}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
        {
            return (false, $"{name}: {ex.Message}");
        }
    }

    private static async Task<(bool, string)> RunAsync(string file, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ServiceTimeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = $"{await output}{await error}".Trim();
            return (process.ExitCode == 0, text.Length > 0 ? text : $"{file} {string.Join(' ', arguments)}: exit {process.ExitCode}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }
}
