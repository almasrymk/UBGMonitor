using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Services;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.Tests;

public sealed class ProcessListCardViewModelTests
{
    [Fact]
    public async Task RefreshAsync_MapsRankedCpuProcesses()
    {
        var handler = new StubHandler
        {
            Json = """[{"name":"chrome","pid":1234,"value":45,"unit":"%"},{"name":"code","pid":5678,"value":22,"unit":"%"}]"""
        };
        using var vm = CreateViewModel(handler, ProcessSortBy.Cpu);

        await vm.RefreshAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(1, vm.Items[0].Rank);
        Assert.Equal("chrome", vm.Items[0].Name);
        Assert.Equal(1234, vm.Items[0].Pid);
        Assert.Equal("45%", vm.Items[0].DisplayValue);
        Assert.Contains("sortBy=cpu", handler.LastUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_NotImplemented_Shows501()
    {
        var handler = new StubHandler { Status = HttpStatusCode.NotImplemented };
        using var vm = CreateViewModel(handler, ProcessSortBy.Network);

        await vm.RefreshAsync();

        Assert.Empty(vm.Items);
        Assert.Equal("Not implemented (501)", vm.ErrorMessage);
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task RefreshAsync_NotFound_Shows404()
    {
        var handler = new StubHandler { Status = HttpStatusCode.NotFound };
        using var vm = CreateViewModel(handler, ProcessSortBy.Cpu);

        await vm.RefreshAsync();

        Assert.Equal("Endpoint not found (404)", vm.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_HttpFailure_ShowsStatus()
    {
        var handler = new StubHandler { Status = HttpStatusCode.InternalServerError };
        using var vm = CreateViewModel(handler, ProcessSortBy.Ram);

        await vm.RefreshAsync();

        Assert.Empty(vm.Items);
        Assert.Equal("Error: HTTP 500", vm.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_ConnectionFailure_ShowsServiceNotRunning()
    {
        var handler = new StubHandler { Throw = new HttpRequestException("No connection could be made") };
        using var vm = CreateViewModel(handler, ProcessSortBy.Cpu);

        await vm.RefreshAsync();

        Assert.Equal("Service not running", vm.ErrorMessage);
    }

    [Fact]
    public async Task RefreshAsync_Canceled_ShowsTimeout()
    {
        var handler = new StubHandler { Throw = new TaskCanceledException() };
        using var vm = CreateViewModel(handler, ProcessSortBy.Ram);

        await vm.RefreshAsync();

        Assert.Equal("Timeout", vm.ErrorMessage);
    }

    [Fact]
    public void Dispose_StopsTimerWithoutThrowing()
    {
        var handler = new StubHandler();
        var vm = CreateViewModel(handler, ProcessSortBy.Cpu, autoStart: true);
        vm.Dispose();
        vm.Dispose();
    }

    private static ProcessListCardViewModel CreateViewModel(StubHandler handler, ProcessSortBy sortBy, bool autoStart = false)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5050") };
        var client = new AgentApiClient(http);
        return new ProcessListCardViewModel(client, "Top 5", sortBy, Brushes.Gray, autoStart);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string Json { get; init; } = "[]";

        public Exception? Throw { get; init; }

        public string LastUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString() ?? string.Empty;
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Json, Encoding.UTF8, "application/json")
            });
        }
    }
}
