using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Runtime;

namespace MonitorAgent.Tests;

public sealed class DeviceEnrollmentClientTests
{
    private const string Url = "https://licensing.test";
    [Fact]
    public void Enrollment_requires_explicit_opt_in_and_preserves_legacy_configuration()
    {
        var values = new Dictionary<string, string?>
        {
            ["LicensingClient:ClientId"] = "legacy-client",
            ["LicensingClient:ClientSecret"] = "TEST-ONLY-legacy-secret",
            ["DeviceEnrollment:DeviceId"] = "TEST-DEVICE-01",
            ["DeviceEnrollment:ClientId"] = "device-client",
            ["DeviceEnrollment:ClientSecret"] = "TEST-ONLY-device-secret"
        };
        Assert.Null(LicensingOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).DeviceCredential);
        values["DeviceEnrollment:Enabled"] = "true";
        var configured = LicensingOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal("device-client", configured.DeviceCredential!.ClientId);
        Assert.Equal("legacy-client", configured.ClientId);
        Assert.False(configured.DeviceCredential.Matches("TEST-DEVICE-01", configured));
    }
    private sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        public readonly List<string> Calls = new();
        public bool RejectToken;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("client-token"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("device-client", body.RootElement.GetProperty("clientId").GetString());
                Assert.Equal("TEST-ONLY-device-secret", body.RootElement.GetProperty("clientSecret").GetString());
                return new HttpResponseMessage(RejectToken ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
                { Content = new StringContent("{\"accessToken\":\"fixture-token\",\"expiresIn\":3600}") };
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fixture-token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"valid\":true}") };
        }
    }

    private static LicensePlatformClient Client(Transport transport, DeviceEnrollmentCredential? credential, bool legacy = false) =>
        new(transport, new AgentIdentity(Options.Create(new AgentOptions())), Options.Create(new LicensingOptions
        {
            PlatformUrl = Url, ProductCode = "000001", DeviceCredential = credential,
            ClientId = legacy ? "device-client" : "", ClientSecret = legacy ? "TEST-ONLY-device-secret" : ""
        }), NullLogger<LicensePlatformClient>.Instance);

    private static DeviceEnrollmentCredential Credential(string device = "TEST-DEVICE-01", string product = "000001", string url = Url) =>
        new(device, product, url, "device-client", "TEST-ONLY-device-secret");

    [Theory]
    [InlineData("OTHER-DEVICE-01", "000001", Url)]
    [InlineData("TEST-DEVICE-01", "other", Url)]
    [InlineData("TEST-DEVICE-01", "000001", "https://other.test")]
    [InlineData("TEST-DEVICE-01", "000001", "http://licensing.test")]
    public async Task Mismatched_enrollment_sends_no_credential_and_never_falls_back(string device, string product, string url)
    {
        using var transport = new Transport();
        var client = Client(transport, Credential(device, product, url), legacy: true);
        Assert.Equal("DEVICE_ENROLLMENT_INVALID", (await client.ActivateAsync("fixture-key", "TEST-DEVICE-01", default)).ErrorCode);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task Device_and_legacy_credentials_preserve_bearer_authentication_and_token_reuse()
    {
        foreach (var enrolled in new[] { true, false })
        {
            using var transport = new Transport();
            var client = Client(transport, enrolled ? Credential() : null, legacy: !enrolled);
            Assert.Equal(LicenseReplyKind.Accepted, (await client.ActivateAsync("fixture-key", "TEST-DEVICE-01", default)).Kind);
            await client.CheckAsync("fixture-key", "TEST-DEVICE-01", true, default);
            await client.DeactivateAsync("fixture-key", "TEST-DEVICE-01", default);
            Assert.Equal(new[] { "/api/v1/auth/client-token", "/api/v1/licensing/activate", "/api/v1/licensing/heartbeat", "/api/v1/licensing/deactivate" }, transport.Calls);
        }
    }

    [Fact]
    public async Task Missing_or_refused_credentials_do_not_send_anonymous_device_requests()
    {
        using var transport = new Transport { RejectToken = true };
        Assert.Equal("DEVICE_CREDENTIAL_REQUIRED", (await Client(transport, null).ActivateAsync("fixture-key", "TEST-DEVICE-01", default)).ErrorCode);
        Assert.Empty(transport.Calls);
        Assert.Equal("DEVICE_AUTH_UNAVAILABLE", (await Client(transport, Credential()).ActivateAsync("fixture-key", "TEST-DEVICE-01", default)).ErrorCode);
        Assert.Single(transport.Calls);
        Assert.DoesNotContain("TEST-ONLY-device-secret", Credential().ToString());
    }
}
