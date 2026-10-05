using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Tests;

public sealed class LicensingTests : IDisposable
{
    private const string Kid = "lk-test-1";
    private const string ProductCode = "000001";
    private static readonly DateTime T0 = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _deviceId = DeviceFingerprint.Get(ProductCode);
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"license-test-{Guid.NewGuid():N}.json");
    private readonly ManualTime _time = new() { Now = T0 };
    private readonly FakeGateway _gateway = new();
    private readonly RecordingHealthStore _health = new();

    public LicensingTests()
    {
        _gateway.SigningKeys = Jwks(_signingKey, Kid);
    }

    public void Dispose()
    {
        _signingKey.Dispose();
        File.Delete(_storePath);
    }

    [Fact]
    public void Verify_AcceptsTokenSignedWithPublishedKey()
    {
        var token = Sign(Claims());

        var claims = LicenseToken.Verify(token, SigningKeySet.Parse(Jwks(_signingKey, Kid)), out var error);

        Assert.Null(error);
        Assert.NotNull(claims);
        Assert.Equal(_deviceId, claims.DeviceId);
        Assert.Equal(ProductCode, claims.ProductCode);
        Assert.Equal("LIC-2026-000123", claims.LicenseNumber);
        Assert.Equal(["monitoring.remote-control"], claims.Features);
        Assert.Equal(20, claims.Limits["max.monitor-points"]);
        Assert.Equal(T0.AddYears(1), claims.LicenseExpiresAtUtc);
    }

    [Fact]
    public void Verify_RejectsChangedPayload()
    {
        var parts = Sign(Claims()).Split('.');
        var forged = Claims();
        forged["license_expires_at"] = new DateTimeOffset(T0.AddYears(50)).ToUnixTimeSeconds();
        var tampered = $"{parts[0]}.{Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(forged))}.{parts[2]}";

        Assert.Null(LicenseToken.Verify(tampered, SigningKeySet.Parse(Jwks(_signingKey, Kid)), out var error));
        Assert.Contains("signature", error);
    }

    [Fact]
    public void Verify_RejectsTokenFromAnotherKey()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Null(LicenseToken.Verify(Sign(Claims()), SigningKeySet.Parse(Jwks(other, Kid)), out _));
        Assert.Null(LicenseToken.Verify(Sign(Claims()), SigningKeySet.Parse(Jwks(_signingKey, "lk-other")), out var error));
        Assert.Contains("unknown key", error);
    }

    [Fact]
    public void SigningKeys_ReadsThePlatformResponse()
    {
        const string platformAnswer = """
            {"keys":[{"kty":"EC","crv":"P-256","x":"QHBZoBCFA8PpCKS5Oy7OSKIa_-G3pxmAv_FW6AgDqPU","y":"-8BOTn2TMx7ceUyBDqL0DtL1d36B2vV5d_EeVR4kePY","kid":"lk-20261004-c9ee25e7","alg":"ES256","use":"sig","status":"active"},
            {"kty":"EC","crv":"P-256","x":"t_0Y2CVKkabIA11BMuYx5Bp2J4UgPwJW-a3zcXe6JAk","y":"7X0InHlafnyqrujjeY7zPq3BZ3XLohdbsyCNYvWcNIw","kid":"lk-20261004-92465dd4","alg":"ES256","use":"sig","status":"retiring"}],
            "pem":[{"kid":"lk-20261004-c9ee25e7","status":"active","publicKeyPem":"-----BEGIN PUBLIC KEY-----\nMFkw...\n-----END PUBLIC KEY-----"}]}
            """;

        var keys = SigningKeySet.Parse(platformAnswer);

        Assert.Equal(2, keys.Count);
        Assert.True(keys.Contains("lk-20261004-c9ee25e7"));
        Assert.True(keys.TryGet("lk-20261004-92465dd4", out var retiring));
        using var ecdsa = ECDsa.Create(retiring);
        Assert.Equal(256, ecdsa.KeySize);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"valid":true,"signedLicenseToken":"a.b.c","nextCheckAfterSeconds":21600}""", LicenseReplyKind.Accepted, null)]
    [InlineData(HttpStatusCode.OK, """{"valid":false,"status":"Suspended"}""", LicenseReplyKind.Rejected, "LIC_LICENSE_SUSPENDED")]
    [InlineData(HttpStatusCode.Conflict, """{"status":409,"code":"LIC_LICENSE_REVOKED","title":"License revoked"}""", LicenseReplyKind.Rejected, "LIC_LICENSE_REVOKED")]
    [InlineData(HttpStatusCode.NotFound, "", LicenseReplyKind.Rejected, "LIC_INVALID_LICENSE")]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":401,"code":"AUTH_UNAUTHORIZED"}""", LicenseReplyKind.Unavailable, "AUTH_UNAUTHORIZED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>down</html>", LicenseReplyKind.Unavailable, null)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"status":429,"code":"RATE_LIMITED"}""", LicenseReplyKind.Unavailable, "RATE_LIMITED")]
    public void Interpret_SeparatesRefusalsFromServerProblems(HttpStatusCode status, string body, LicenseReplyKind kind, string? code)
    {
        var reply = LicensePlatformClient.Interpret(status, body);

        Assert.Equal(kind, reply.Kind);
        Assert.Equal(code, reply.ErrorCode);
    }

    [Fact]
    public void Interpret_ReadsTokenAndNextCheck()
    {
        var reply = LicensePlatformClient.Interpret(HttpStatusCode.OK, """{"valid":true,"signedLicenseToken":"a.b.c","nextCheckAfterSeconds":3600,"licenseNumber":"LIC-1"}""");

        Assert.Equal("a.b.c", reply.Token);
        Assert.Equal(3600, reply.NextCheckAfterSeconds);
        Assert.Equal("LIC-1", reply.LicenseNumber);
    }

    [Fact]
    public async Task Activate_WithSignedToken_IsActiveAndKeepsOnlyThePrefix()
    {
        _gateway.Next = Accepted();
        var service = CreateService();

        var result = await service.ActivateAsync(" abcd-efgh-ijkl-mnop-qrst ", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(LicenseState.Active, result.Status.State);
        Assert.True(result.Status.IsValid);
        Assert.Equal("ABCD-…", result.Status.KeyPrefix);
        Assert.Equal("ABCD-EFGH-IJKL-MNOP-QRST", _gateway.LastProductKey);
        Assert.Equal(_deviceId, _gateway.LastDeviceId);
        Assert.DoesNotContain("ABCD-EFGH", File.ReadAllText(_storePath));
        Assert.True(service.IsLicensed);
        Assert.Null(_health.Severity);
    }

    [Fact]
    public void WithoutALicense_ResultsAreHiddenAndTheNoticeIsRaised()
    {
        var service = CreateService();

        Assert.Equal(LicenseState.NotActivated, service.GetStatus().State);
        Assert.False(service.IsLicensed);
        Assert.Equal("Critical", _health.Severity);
        Assert.Contains("Monitoring continues in the background", _health.Message);
    }

    [Fact]
    public async Task Offline_KeepsWorkingUntilTheGracePeriodEnds()
    {
        _gateway.Next = Accepted();
        var service = CreateService();
        await service.ActivateAsync("ABCD-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        _gateway.Next = new LicenseReply(LicenseReplyKind.Unavailable, "No internet.");
        _time.Now = T0.AddDays(1);
        var offline = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Offline, offline.State);
        Assert.True(offline.IsValid);
        Assert.Equal(T0.AddDays(2), offline.OfflineUntilUtc);

        _time.Now = T0.AddDays(2).AddHours(1);
        var tooLong = service.GetStatus();

        Assert.Equal(LicenseState.Offline, tooLong.State);
        Assert.False(tooLong.IsValid);
        Assert.False(service.IsLicensed);
        Assert.Equal("Critical", _health.Severity);
    }

    [Fact]
    public async Task Offline_StateSurvivesARestart()
    {
        _gateway.Next = Accepted();
        await CreateService().ActivateAsync("ABCD-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        _time.Now = T0.AddDays(1);
        var restarted = CreateService().GetStatus();

        Assert.True(restarted.IsValid);
        Assert.Equal("LIC-2026-000123", restarted.LicenseNumber);
    }

    [Fact]
    public async Task Revoked_DropsTheKeyAndHidesResults()
    {
        _gateway.Next = Accepted();
        var service = CreateService();
        await service.ActivateAsync("ABCD-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        _gateway.Next = new LicenseReply(LicenseReplyKind.Rejected, "License revoked", "LIC_LICENSE_REVOKED");
        var status = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(LicenseState.Revoked, status.State);
        Assert.False(status.IsValid);
        Assert.False(service.IsLicensed);
        Assert.Equal(LicenseState.Revoked, CreateService().GetStatus().State);
        Assert.Null(LicenseStore.ProductKey(new LicenseStore(_storePath, NullLogger<LicenseStore>.Instance).Load()));
    }

    [Fact]
    public async Task ActivationRefused_KeepsTheCurrentLicense()
    {
        _gateway.Next = Accepted();
        var service = CreateService();
        await service.ActivateAsync("ABCD-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        _gateway.Next = new LicenseReply(LicenseReplyKind.Rejected, "limit", "LIC_ACTIVATION_LIMIT_REACHED");
        var result = await service.ActivateAsync("WXYZ-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("maximum number of computers", result.Message);
        Assert.Equal(LicenseState.Active, result.Status.State);
        Assert.Equal("ABCD-…", result.Status.KeyPrefix);
    }

    [Fact]
    public async Task ClockMovedBack_IsNotValid()
    {
        _gateway.Next = Accepted();
        var service = CreateService();
        await service.ActivateAsync("ABCD-EFGH-IJKL-MNOP-QRST", CancellationToken.None);

        _time.Now = T0.AddDays(1);
        _gateway.Next = new LicenseReply(LicenseReplyKind.Unavailable, "No internet.");
        await service.RefreshAsync(CancellationToken.None);
        _time.Now = T0.AddHours(-2);

        var status = CreateService().GetStatus();

        Assert.False(status.IsValid);
        Assert.Contains("clock", status.Message);
    }

    [Fact]
    public void Options_ReadOnlyTheApiClientFromSettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LicensingClient:ClientId"] = " monitor-agent ",
            ["LicensingClient:ClientSecret"] = SecretProtector.Protect("s3cret"),
            ["LicensingClient:PlatformUrl"] = "https://evil.test",
            ["Licensing:Enabled"] = "false"
        }).Build();

        var options = LicensingOptions.FromConfiguration(configuration);

        Assert.Equal("monitor-agent", options.ClientId);
        Assert.Equal("s3cret", options.ClientSecret);
        Assert.Equal("https://almasrymk-001-site15.etempurl.com", options.PlatformUrl);
        Assert.Equal(ProductCode, options.ProductCode);
    }

    [Fact]
    public void Options_ReadTheProductCodeFromSettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LicensingClient:ProductCode"] = " 000002 "
        }).Build();

        Assert.Equal("000002", LicensingOptions.FromConfiguration(configuration).ProductCode);
    }

    [Fact]
    public void DeviceId_IsStableAndPerProduct()
    {
        Assert.Equal(DeviceFingerprint.Hash("MONITOR_AGENT", "abc"), DeviceFingerprint.Hash("monitor_agent", " ABC "));
        Assert.NotEqual(DeviceFingerprint.Hash("MONITOR_AGENT", "abc"), DeviceFingerprint.Hash("OTHER", "abc"));
        Assert.StartsWith("ma-", DeviceFingerprint.Hash("MONITOR_AGENT", "abc"));
    }

    private LicenseService CreateService()
        => new(
            _gateway,
            new LicenseStore(_storePath, NullLogger<LicenseStore>.Instance),
            _health,
            Options.Create(new LicensingOptions { OfflineGraceDays = 7 }),
            _time,
            NullLogger<LicenseService>.Instance);

    private LicenseReply Accepted(int limit = 20)
        => new(LicenseReplyKind.Accepted, "Accepted.", Token: Sign(Claims(limit)), NextCheckAfterSeconds: 21600, LicenseNumber: "LIC-2026-000123");

    private Dictionary<string, object> Claims(int limit = 20) => new()
    {
        ["license_id"] = "8a3c",
        ["license_number"] = "LIC-2026-000123",
        ["product_code"] = ProductCode,
        ["device_id"] = _deviceId,
        ["iat"] = new DateTimeOffset(T0).ToUnixTimeSeconds(),
        ["exp"] = new DateTimeOffset(T0.AddDays(3)).ToUnixTimeSeconds(),
        ["license_expires_at"] = new DateTimeOffset(T0.AddYears(1)).ToUnixTimeSeconds(),
        ["offline_grace_days"] = 2,
        ["features"] = new[] { "monitoring.remote-control" },
        ["limits"] = new Dictionary<string, int> { ["max.monitor-points"] = limit }
    };

    private string Sign(Dictionary<string, object> claims)
    {
        var header = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "JWT", kid = Kid }));
        var payload = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = _signingKey.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
        return $"{header}.{payload}.{Base64Url.Encode(signature)}";
    }

    private static string Jwks(ECDsa key, string kid)
    {
        var parameters = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "EC", crv = "P-256", x = Base64Url.Encode(parameters.Q.X!), y = Base64Url.Encode(parameters.Q.Y!), kid, alg = "ES256", use = "sig", status = "active" }
            }
        });
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTime Now { get; set; }

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class FakeGateway : ILicensePlatformClient
    {
        public LicenseReply Next { get; set; } = new(LicenseReplyKind.Unavailable, "Not set.");
        public string? SigningKeys { get; set; }
        public string? LastProductKey { get; private set; }
        public string? LastDeviceId { get; private set; }

        public Task<LicenseReply> ActivateAsync(string productKey, string deviceId, CancellationToken cancellationToken) => Reply(productKey, deviceId);

        public Task<LicenseReply> CheckAsync(string productKey, string deviceId, bool heartbeat, CancellationToken cancellationToken) => Reply(productKey, deviceId);

        public Task<LicenseReply> DeactivateAsync(string productKey, string deviceId, CancellationToken cancellationToken) => Reply(productKey, deviceId);

        public Task<string?> GetSigningKeysAsync(CancellationToken cancellationToken) => Task.FromResult(SigningKeys);

        private Task<LicenseReply> Reply(string productKey, string deviceId)
        {
            LastProductKey = productKey;
            LastDeviceId = deviceId;
            return Task.FromResult(Next);
        }
    }

    private sealed class RecordingHealthStore : IMonitorHealthStore
    {
        public string? Severity { get; private set; }
        public string? Message { get; private set; }

        public void SetIssue(string key, string severity, string title, string message, string? monitorPointId = null)
            => (Severity, Message) = (severity, message);

        public void ClearIssue(string key) => (Severity, Message) = (null, null);

        public void SetPointHealth(string monitorPointId, bool isUp, string? message = null, string? status = null, double? responseMs = null) { }
        public bool? GetIsUp(string monitorPointId) => null;
        public double? GetResponseMs(string monitorPointId) => null;
        public DateTime? GetStatusSinceUtc(string monitorPointId) => null;
        public string? GetStatus(string monitorPointId) => null;
        public DateTime? GetLastCheckedUtc(string monitorPointId) => null;
        public string? GetMessage(string monitorPointId) => null;
        public IReadOnlyList<AgentIssueDto> GetIssues() => [];
    }
}
