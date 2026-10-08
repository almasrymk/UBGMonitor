using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorAgent.Service.Licensing;
using MonitorAgent.Service.Options;
using MonitorAgent.Service.Runtime;

namespace MonitorAgent.Tests;

public sealed class LicenseLoggingTests
{
    private sealed class Transport : HttpMessageHandler, IHttpClientFactory
    {
        public bool Fail = true;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Fail ? throw new HttpRequestException("TEST-ONLY-sensitive Password=fixture https://user:fixture@fixture.test/") :
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"keys\":[]}") });
    }
    private sealed class RecordingLog : ILogger<LicensePlatformClient>
    {
        public readonly List<string> Lines = [];
        public IDisposable? BeginScope<T>(T state) where T : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<T>(LogLevel level, EventId id, T state, Exception? exception, Func<T, Exception?, string> formatter) => Lines.Add(formatter(state, exception) + exception);
    }
    [Fact]
    public async Task Signing_key_failure_does_not_log_exception_secrets_and_next_request_can_succeed()
    {
        using var transport = new Transport();
        var log = new RecordingLog();
        var gateway = new LicensePlatformClient(transport, new AgentIdentity(Options.Create(new AgentOptions())),
            Options.Create(new LicensingOptions { PlatformUrl = "https://fixture.test" }), log);
        Assert.Null(await gateway.GetSigningKeysAsync(default));
        var text = string.Join("\n", log.Lines);
        Assert.Contains(nameof(HttpRequestException), text);
        Assert.DoesNotContain("TEST-ONLY-sensitive", text);
        Assert.DoesNotContain("Password", text);
        Assert.DoesNotContain("user:fixture", text);
        transport.Fail = false;
        Assert.Equal("{\"keys\":[]}", await gateway.GetSigningKeysAsync(default));
    }
}
