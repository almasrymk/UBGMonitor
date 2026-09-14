using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ClientAgent.Shared.Constants;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.Services;

public sealed class AgentApiClient
{
    private readonly HttpClient _httpClient;

    public AgentApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://127.0.0.1:5050");
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public Task<AgentStatusDto?> GetStatusAsync(CancellationToken cancellationToken = default)
        => GetAsync<AgentStatusDto>(ApiRoutes.Status, cancellationToken);

    public Task<SystemSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => GetAsync<SystemSnapshot>(ApiRoutes.Snapshot, cancellationToken);

    public Task<CpuInfo?> GetCpuAsync(CancellationToken cancellationToken = default)
        => GetAsync<CpuInfo>(ApiRoutes.Cpu, cancellationToken);

    public Task<RamInfo?> GetRamAsync(CancellationToken cancellationToken = default)
        => GetAsync<RamInfo>(ApiRoutes.Ram, cancellationToken);

    public Task<List<DiskPartition>?> GetPartitionsAsync(CancellationToken cancellationToken = default)
        => GetAsync<List<DiskPartition>>(ApiRoutes.DiskPartitions, cancellationToken);

    public Task<NetworkInfo?> GetNetworkAsync(CancellationToken cancellationToken = default)
        => GetAsync<NetworkInfo>(ApiRoutes.Network, cancellationToken);

    private async Task<T?> GetAsync<T>(string route, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(route, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);
    }
}
