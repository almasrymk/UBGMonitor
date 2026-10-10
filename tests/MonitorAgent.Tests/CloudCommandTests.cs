using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using MonitorAgent.Cloud;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Tests;

/// <summary>AG-13 (M11): every check of a remote action, the local switch, the six types and the guaranteed result.</summary>
public sealed class CloudCommandTests : IDisposable
{
    private const string Kid = "test-key";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "monitoragent-command-tests", Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Guid _device = Guid.NewGuid();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

    public CloudCommandTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _key.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file for a moment.
        }
    }

    private string Jwks()
    {
        var p = _key.ExportParameters(false);
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "EC", crv = "P-256", kid = Kid, x = B64(p.Q.X!), y = B64(p.Q.Y!), use = "sig", alg = "ES256" } } });
    }

    private CommandVerifier Verifier(bool allow = true, Outbox? store = null) => new(_device, Jwks(), allow, _clock, store);

    private Command Signed(string type = "refresh-inventory", string parameters = "{}", Guid? device = null, TimeSpan? lifetime = null, string? nonce = null, string kid = Kid)
    {
        var id = Guid.NewGuid();
        var expires = DateTimeOffset.FromUnixTimeMilliseconds(_clock.GetUtcNow().Add(lifetime ?? TimeSpan.FromMinutes(5)).ToUnixTimeMilliseconds());
        nonce ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var payload = CommandVerifier.Payload(id, type, parameters, expires, nonce, device ?? _device);
        var signature = _key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new Command { CommandId = id.ToString("N"), Type = type, ParametersJson = parameters, ExpiresAt = Timestamp.FromDateTimeOffset(expires), Nonce = nonce, Signature = ByteString.CopyFrom(signature), KeyId = kid };
    }

    [Fact]
    public void A_valid_command_passes() => Assert.Null(Verifier().Check(Signed()));

    [Fact]
    public void A_bad_signature_is_refused()
    {
        var command = Signed();
        command.ParametersJson = "{\"service\":\"Spooler\"}";

        Assert.Equal((CommandStatus.Rejected, "Invalid signature."), Verifier().Check(command));
    }

    [Fact]
    public void A_command_for_another_device_is_refused() =>
        Assert.Equal((CommandStatus.Rejected, "Invalid signature."), Verifier().Check(Signed(device: Guid.NewGuid())));

    [Fact]
    public void An_expired_command_is_refused()
    {
        var command = Signed(lifetime: TimeSpan.FromMinutes(1));
        _clock.Now = _clock.Now.AddMinutes(2);

        Assert.Equal((CommandStatus.Expired, "The command has expired."), Verifier().Check(command));
    }

    [Fact]
    public void An_expiry_too_far_ahead_is_refused() =>
        Assert.Equal((CommandStatus.Rejected, "The expiry is more than 5 minutes ahead."), Verifier().Check(Signed(lifetime: TimeSpan.FromHours(1))));

    [Fact]
    public void A_replayed_nonce_is_refused_even_after_a_restart()
    {
        using var store = new Outbox(Path.Combine(_folder, "cloud.db"), 10 * 1024 * 1024);
        var command = Signed(nonce: "00112233445566778899aabbccddeeff");
        Assert.Null(Verifier(store: store).Check(command));

        Assert.Equal((CommandStatus.Rejected, "Replayed nonce."), Verifier(store: store).Check(Signed(nonce: "00112233445566778899aabbccddeeff")));

        _clock.Now = _clock.Now.AddMinutes(11);
        Assert.Null(Verifier(store: store).Check(Signed(nonce: "00112233445566778899aabbccddeeff")));
    }

    [Fact]
    public void Unknown_keys_and_a_device_enrolled_without_keys_refuse()
    {
        Assert.Equal((CommandStatus.Rejected, "Unknown signing key."), Verifier().Check(Signed(kid: "other")));
        Assert.Equal((CommandStatus.Rejected, "Unknown signing key."), new CommandVerifier(_device, null, true, _clock).Check(Signed()));
    }

    [Fact]
    public void The_local_switch_refuses_everything()
    {
        var off = Verifier(allow: false);
        foreach (var type in CommandRunner.Types)
            Assert.Equal((CommandStatus.Rejected, "Remote actions are turned off on this device."), off.Check(Signed(type)));
    }

    [Fact]
    public void The_switch_is_off_by_default() => Assert.False(new CloudOptions().AllowRemoteActions);

    [Fact]
    public async Task The_runner_runs_the_six_types_and_queues_one_guaranteed_result_each()
    {
        using var outbox = new Outbox(Path.Combine(_folder, "run.db"), 10 * 1024 * 1024);
        var executor = new RecordingExecutor();
        var inventory = 0;
        var runner = new CommandRunner(Verifier(), executor, outbox, () => inventory++, _clock);

        var results = new List<CommandResult>
        {
            await runner.RunAsync(Signed("refresh-inventory"), CancellationToken.None),
            await runner.RunAsync(Signed("run-speed-test"), CancellationToken.None),
            await runner.RunAsync(Signed("restart-agent"), CancellationToken.None),
            await runner.RunAsync(Signed("service-start", "{\"service\":\"Spooler\"}"), CancellationToken.None),
            await runner.RunAsync(Signed("service-stop", "{\"service\":\"Spooler\"}"), CancellationToken.None),
            await runner.RunAsync(Signed("service-restart", "{\"service\":\"W3SVC\"}"), CancellationToken.None),
        };

        Assert.All(results, r => Assert.Equal(CommandStatus.Succeeded, r.Status));
        Assert.Equal(1, inventory);
        Assert.Equal(["run-speed-test:", "restart-agent:", "service-start:Spooler", "service-stop:Spooler", "service-restart:W3SVC"], executor.Calls);
        var queued = outbox.Pending(0).Select(r => r.Message.CommandResult).ToList();
        Assert.Equal(results.Select(r => r.CommandId), queued.Select(q => q.CommandId));
    }

    [Fact]
    public async Task The_runner_refuses_bad_service_names_unknown_types_and_reports_failures()
    {
        using var outbox = new Outbox(Path.Combine(_folder, "bad.db"), 10 * 1024 * 1024);
        var executor = new RecordingExecutor { Fail = true };
        var runner = new CommandRunner(Verifier(), executor, outbox, () => { }, _clock);

        Assert.Equal(CommandStatus.Rejected, (await runner.RunAsync(Signed("service-stop", "{\"service\":\"x & del *\"}"), CancellationToken.None)).Status);
        Assert.Equal(CommandStatus.Rejected, (await runner.RunAsync(Signed("service-stop"), CancellationToken.None)).Status);
        Assert.Equal(CommandStatus.Rejected, (await runner.RunAsync(Signed("format-disk"), CancellationToken.None)).Status);
        var failed = await runner.RunAsync(Signed("run-speed-test"), CancellationToken.None);
        Assert.Equal((CommandStatus.Failed, "it broke"), (failed.Status, failed.Output));
        Assert.Empty(executor.Calls.Where(c => c.StartsWith("service-", StringComparison.Ordinal)));
        Assert.Equal(4, outbox.Pending(0).Count());
    }

    [Fact]
    public void Enrollment_keys_are_kept_in_the_state_file()
    {
        var store = new CloudStateStore(_folder);
        var state = CloudStateStore.Enrolled(new CloudState(), new EnrollmentResult(_device, "TEST-ONLY-secret", "https://gateway.test", "Acme", "Cairo", "Licensed", null, null, Jwks()));
        store.Save(state);

        Assert.Equal(Jwks(), store.Load().CommandSigningKeys);
        Assert.Equal(1, new CommandVerifier(_device, store.Load().CommandSigningKeys, true, _clock).KeyCount);
    }

    private sealed class RecordingExecutor : ICloudCommandExecutor
    {
        public List<string> Calls { get; } = [];

        public bool Fail { get; init; }

        public Task<(bool Success, string Output)> ExecuteAsync(string type, string? service, CancellationToken cancellationToken)
        {
            Calls.Add($"{type}:{service}");
            return Task.FromResult(Fail ? (false, "it broke") : (true, "ok"));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
