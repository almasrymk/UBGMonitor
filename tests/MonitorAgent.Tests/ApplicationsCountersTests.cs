using System.Reflection;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Tests;

public sealed class ApplicationsCountersTests
{
    [Fact]
    public void Counters_use_all_inventory_while_search_filters_rows_and_unload_clears_them()
    {
        var model = new ApplicationsViewModel(new AgentApiClient());
        Set(model, "_programs", new InstalledProgramDto[] { new() { Name = "Browser", IsRunning = true }, new() { Name = "Editor" } });
        Set(model, "_users", new UserAccountDto[] { new() { UserName = "Owner", IsSignedIn = true, IsAdmin = true }, new() { UserName = "Guest", Enabled = false } });
        Set(model, "_services", new SystemServiceDto[] { new() { Name = "Worker", State = "Running" }, new() { Name = "StoppedWorker", State = "Stopped", Problem = "Stopped" } });
        model.SearchText = "Browser";
        Assert.Single(model.Programs);
        Assert.Equal(2, model.ProgramsTotal);
        Assert.Equal(1, model.ProgramsNotRunning);
        Assert.Equal(2, model.UsersTotal);
        Assert.Equal(1, model.UsersSignedIn);
        Assert.Equal(1, model.UsersDisabled);
        Assert.Equal(1, model.UsersAdministrators);
        Assert.Equal(2, model.ServicesTotal);
        Assert.Equal(1, model.ServicesRunning);
        Assert.Equal(1, model.ServicesStopped);
        Assert.Equal(1, model.ServicesProblems);
        Assert.Equal("Showing 1 of 2 applications", model.VisibleRowsSummary);
        model.Unload();
        Assert.Equal(0, model.ProgramsTotal);
        Assert.Equal(0, model.UsersTotal);
        Assert.Equal(0, model.ServicesTotal);
    }

    private static void Set(ApplicationsViewModel model, string field, object value)
        => typeof(ApplicationsViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, value);
}
