using ClientAgent.Service.Config;
using ClientAgent.Shared.Models;

namespace ClientAgent.Tests;

internal sealed class FakeLocalConfigCache : ILocalConfigCache
{
    private AgentRuntimeConfig _config = new();

    public GeneralRuntimeSettings General { get; } = new();

    public Task<AgentRuntimeConfig> GetConfigAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_config);

    public Task SaveConfigAsync(AgentRuntimeConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        return Task.CompletedTask;
    }

    public string GetConfigVersion() => _config.ConfigVersion;

    public DateTime? GetLastSyncUtc() => null;

    public GeneralRuntimeSettings GetGeneral() => General;
}
