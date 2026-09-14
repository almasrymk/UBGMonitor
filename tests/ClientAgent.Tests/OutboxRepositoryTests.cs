using ClientAgent.Service.Outbox;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClientAgent.Tests;

public sealed class OutboxRepositoryTests : IDisposable
{
    private readonly string _path;
    private readonly OutboxRepository _repository;

    public OutboxRepositoryTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"outbox-{Guid.NewGuid():N}.db");
        _repository = new OutboxRepository(_path, NullLogger<OutboxRepository>.Instance);
        _repository.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Write_ThenGetPending_ReturnsEventWithSameId()
    {
        var monitoringEvent = CreateEvent();

        await _repository.WriteAsync(monitoringEvent);
        var pending = await _repository.GetPendingAsync(10);

        Assert.Single(pending);
        Assert.Equal(monitoringEvent.EventId, pending[0].EventId);
        Assert.Equal(1, await _repository.GetPendingCountAsync());
    }

    [Fact]
    public async Task Write_SameEventId_IsIdempotent()
    {
        var monitoringEvent = CreateEvent();

        await _repository.WriteAsync(monitoringEvent);
        await _repository.WriteAsync(monitoringEvent);

        Assert.Equal(1, await _repository.GetPendingCountAsync());
    }

    [Fact]
    public async Task MarkAsSent_RemovesEventFromPending()
    {
        var monitoringEvent = CreateEvent();
        await _repository.WriteAsync(monitoringEvent);

        await _repository.MarkAsSentAsync(monitoringEvent.EventId, "Central");

        Assert.Empty(await _repository.GetPendingAsync(10));
        Assert.Equal(0, await _repository.GetPendingCountAsync());
        Assert.Single(await _repository.GetRecentAsync(10));
    }

    private static MonitoringEvent CreateEvent() => new()
    {
        EventId = Guid.NewGuid(),
        AgentId = "agent-1",
        MonitorPointId = "cpu",
        EventType = EventType.ResourceThreshold,
        Severity = Severity.Critical,
        Status = EventStatus.Critical,
        Message = "CPU high"
    };

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // Ignore locked SQLite files on Windows.
        }
    }
}
