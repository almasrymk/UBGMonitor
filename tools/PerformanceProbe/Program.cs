using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MonitorAgent.Desktop;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;
using System.Runtime.CompilerServices;

AppBuilder.Configure<App>().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
using var http = new HttpClient(new OfflineHandler()) { BaseAddress = new Uri("http://127.0.0.1:5050") };
using var vm = new MainViewModel(new AgentApiClient(http));
var window = new MainWindow(vm);
window.Width = 1400;
window.Height = 900;
window.Show();
vm.Settings.MachineName = "Unsaved test value";
var host = window.FindControl<ContentControl>("ActivePage")!;
Console.WriteLine($"Initial active page: {host.Content?.GetType().Name}");
VerifyRejectedNavigation(vm, host);
var refs = new List<WeakReference>();
for (int cycle = 0; cycle < 20; cycle++)
{
    foreach (var tab in new[] { "Settings", "Applications", "Monitor Points", "Reports", "About", "Dashboard" })
        Switch(window, vm, host, tab, refs);
    Dispatcher.UIThread.RunJobs();
    if (cycle == 4 || cycle == 19)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine($"Cycle {cycle + 1}: old pages alive {refs.Count(r => r.IsAlive)}/{refs.Count}, managed bytes {GC.GetTotalMemory(true)}");
        if (refs.Any(r => r.IsAlive)) throw new Exception("A departed page is still retained");
    }
}
if (vm.Settings.MachineName != "Unsaved test value") throw new Exception("Unsaved settings lost");
Console.WriteLine("Unsaved setting retained");
window.Close();

[MethodImpl(MethodImplOptions.NoInlining)]
static void Switch(MainWindow window, MainViewModel vm, ContentControl host, string tab, List<WeakReference> refs)
{
    refs.Add(new WeakReference(host.Content));
    vm.SelectedTab = tab;
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
}
[MethodImpl(MethodImplOptions.NoInlining)]
static void VerifyRejectedNavigation(MainViewModel vm, ContentControl host)
{
    vm.SelectedTab = "Settings";
    var settingsPage = host.Content;
    var commit = vm.Settings.CommitPendingEdits;
    vm.Settings.CommitPendingEdits = () => false;
    vm.SelectTabCommand.ExecuteAsync("Applications").GetAwaiter().GetResult();
    if (!ReferenceEquals(settingsPage, host.Content)) throw new Exception("Rejected navigation recreated the editor");
    vm.Settings.CommitPendingEdits = commit;
    Console.WriteLine("Rejected navigation keeps the same editor");
}
sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}
