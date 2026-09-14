using ClientAgent.Service.Connectivity;
using ClientAgent.Service.Dispatch;
using ClientAgent.Shared.Models;

namespace ClientAgent.Tests;

public sealed class RoutingServiceTests
{
    [Fact]
    public async Task PrefersMadkhalWhenAvailable()
    {
        var tracker = new ConnectivityTracker();
        tracker.SetMadkhal(true);
        tracker.SetCentral(true);
        var sut = new RoutingService(tracker);

        var destination = await sut.DecideDestinationAsync();

        Assert.Equal(Destination.Madkhal, destination);
    }

    [Fact]
    public async Task UsesCentralWhenMadkhalIsDown()
    {
        var tracker = new ConnectivityTracker();
        tracker.SetMadkhal(false);
        tracker.SetCentral(true);
        var sut = new RoutingService(tracker);

        var destination = await sut.DecideDestinationAsync();

        Assert.Equal(Destination.Central, destination);
    }

    [Fact]
    public async Task FallsBackToLocalWhenOffline()
    {
        var tracker = new ConnectivityTracker();
        tracker.SetMadkhal(false);
        tracker.SetCentral(false);
        var sut = new RoutingService(tracker);

        var destination = await sut.DecideDestinationAsync();

        Assert.Equal(Destination.Local, destination);
    }
}
