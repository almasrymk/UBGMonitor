using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace MonitorAgent.Tests;

public sealed class DatabaseTlsTests
{
    private sealed class RecordedIssues : IIssueDataLogger
    {
        public int Count;
        public void Record(string source, IReadOnlyList<AgentIssueDto> issues) => Count++;
        public void RecordResolved(string source, IReadOnlyList<AgentIssueDto> issues) { }
    }
    [Fact]
    public void Compatibility_warning_is_visible_once_and_clears_after_explicit_verification_choice()
    {
        var recorder = new RecordedIssues();
        using var services = new ServiceCollection().AddSingleton<IIssueDataLogger>(recorder).AddSingleton<NotificationTrigger>().BuildServiceProvider();
        var health = new MonitorHealthStore(services);
        var points = new[] { new MonitorPoint { DisplayName = "fixture DB", Type = MonitorPointType.Database, Database = new DatabaseLogin() } };
        DatabaseMonitor.UpdateTlsWarning(points, health);
        DatabaseMonitor.UpdateTlsWarning(points, health);
        Assert.Equal(1, recorder.Count);
        Assert.Equal("Warning", Assert.Single(health.GetIssues()).Severity);
        Assert.Contains("fixture DB", Assert.Single(health.GetIssues()).Message);
        points[0].Database!.TlsMode = DatabaseTlsMode.Verify;
        DatabaseMonitor.UpdateTlsWarning(points, health);
        Assert.Empty(health.GetIssues());
    }
    [Theory]
    [InlineData(DatabaseEngine.SqlServer)]
    [InlineData(DatabaseEngine.PostgreSql)]
    [InlineData(DatabaseEngine.MySql)]
    public void Verify_requires_encryption_and_server_identity_and_legacy_mode_keeps_driver_behavior(DatabaseEngine engine)
    {
        var legacy = JsonSerializer.Deserialize<DatabaseLogin>("{}")!;
        Assert.Equal(DatabaseTlsMode.Compatibility, legacy.TlsMode);
        legacy.Engine = engine;
        legacy.Server = "fixture.test";
        using var compatibility = DatabaseMonitor.CreateConnection(legacy);
        legacy.TlsMode = DatabaseTlsMode.Verify;
        using var verified = DatabaseMonitor.CreateConnection(legacy);
        if (engine == DatabaseEngine.SqlServer)
        {
            var old = new SqlConnectionStringBuilder(compatibility.ConnectionString);
            var secure = new SqlConnectionStringBuilder(verified.ConnectionString);
            Assert.Equal(SqlConnectionEncryptOption.Optional, old.Encrypt);
            Assert.True(old.TrustServerCertificate);
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, secure.Encrypt);
            Assert.False(secure.TrustServerCertificate);
        }
        else if (engine == DatabaseEngine.PostgreSql)
        {
            Assert.Equal(new NpgsqlConnectionStringBuilder().SslMode, new NpgsqlConnectionStringBuilder(compatibility.ConnectionString).SslMode);
            Assert.Equal(SslMode.VerifyFull, new NpgsqlConnectionStringBuilder(verified.ConnectionString).SslMode);
        }
        else
        {
            Assert.Equal(new MySqlConnectionStringBuilder().SslMode, new MySqlConnectionStringBuilder(compatibility.ConnectionString).SslMode);
            Assert.Equal(MySqlSslMode.VerifyFull, new MySqlConnectionStringBuilder(verified.ConnectionString).SslMode);
        }
    }
    [Fact]
    public void Verification_test_resolves_stored_credential_without_changing_saved_mode()
    {
        var stored = JsonNode.Parse("""{"MonitorPoints":[{"MonitorPointId":"db","Type":3,"Database":{"Server":"fixture","Password":"dpapi:TEST-ONLY"}}]}""")!.AsObject();
        var before = stored.ToJsonString();
        var login = DatabaseTestResolver.Resolve(new("db", Verify: true), stored);
        Assert.Equal(DatabaseTlsMode.Verify, login.TlsMode);
        Assert.Equal("dpapi:TEST-ONLY", login.Password);
        Assert.Equal(before, stored.ToJsonString());
        var point = new MonitorPoint { Type = MonitorPointType.Database, Database = new DatabaseLogin { Server = "fixture" } };
        Assert.Contains("server identity not verified", MonitorPointText.Target(point));
        point.Database.TlsMode = DatabaseTlsMode.Verify;
        Assert.DoesNotContain("not verified", MonitorPointText.Target(point));
    }
}
