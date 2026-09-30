using System.Data.Common;
using ClientAgent.Service.Config;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Monitoring;

public interface IConnectivityFilter
{
    /// <summary>
    /// While the device has no network or no internet, drops the issues of checks that need them (monitor points on
    /// other machines, speed test, download / upload), so only the one network or internet problem is reported.
    /// </summary>
    Task<IReadOnlyList<AgentIssueDto>> RemoveNetworkDependentAsync(IReadOnlyList<AgentIssueDto> issues, CancellationToken cancellationToken);
}

public sealed class ConnectivityFilter : IConnectivityFilter
{
    private static readonly HashSet<string> NetworkSpecIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "spec:internet", "spec:download", "spec:upload"
    };

    private readonly IInternetStatus _internet;
    private readonly ILocalConfigCache _config;

    public ConnectivityFilter(IInternetStatus internet, ILocalConfigCache config)
    {
        _internet = internet;
        _config = config;
    }

    public async Task<IReadOnlyList<AgentIssueDto>> RemoveNetworkDependentAsync(
        IReadOnlyList<AgentIssueDto> issues, CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return issues;
        }

        var config = await _config.GetConfigAsync(cancellationToken);
        var dependent = issues.Where(issue => NeedsNetwork(issue, config)).ToList();
        if (dependent.Count == 0)
        {
            return issues;
        }

        // A check on another machine can fail a few seconds before the internet check notices the connection is gone.
        await _internet.RecheckAsync(cancellationToken);
        return _internet.Problem == ConnectivityProblem.None ? issues : issues.Except(dependent).ToList();
    }

    private static bool NeedsNetwork(AgentIssueDto issue, AgentRuntimeConfig config)
    {
        if (issue.Id.StartsWith("network:", StringComparison.OrdinalIgnoreCase) || issue.Id == "internet:connection")
        {
            return false;
        }

        if (issue.Id.StartsWith("internet:", StringComparison.OrdinalIgnoreCase) || NetworkSpecIds.Contains(issue.Id)
            || issue.Id == "madkhal" || issue.MonitorPointId == "madkhal")
        {
            return true;
        }

        if (issue.Id == "database" || issue.MonitorPointId == "local-database")
        {
            return !IsLocal(ServerOf(config.DatabaseConnectionString));
        }

        var point = config.MonitorPoints.FirstOrDefault(p => string.Equals(p.MonitorPointId, issue.MonitorPointId, StringComparison.OrdinalIgnoreCase));
        return point?.Type switch
        {
            MonitorPointType.Website or MonitorPointType.Device or MonitorPointType.Madkhal => !IsLocal(point.Address),
            MonitorPointType.Database => !IsLocal(string.IsNullOrWhiteSpace(point.Database?.Server) ? point.Address : point.Database.Server),
            _ => false
        };
    }

    private static string? ServerOf(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var key in new[] { "Data Source", "Server", "Host", "Address", "Addr" })
            {
                if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } server)
                {
                    return server;
                }
            }
        }
        catch (ArgumentException)
        {
        }

        return null;
    }

    /// <summary>An address on this machine does not need the network: localhost, ".", "(local)", 127.x, the machine name.</summary>
    private static bool IsLocal(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var host = address.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
        }

        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        host = host.Split('\\', ',')[0].Trim('[', ']');
        if (host.Count(c => c == ':') == 1)
        {
            host = host.Split(':')[0];
        }

        return host is "." or "(local)" or "::1"
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.", StringComparison.Ordinal)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }
}
