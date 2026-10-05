using Microsoft.Extensions.Options;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Licensing;

public interface ILicenseState
{
    LicenseStatusDto GetStatus();

    /// <summary>Whether results may be shown. Monitoring itself always runs, so nothing is missing once a license is activated.</summary>
    bool IsLicensed { get; }

    Task<LicenseActionResultDto> ActivateAsync(string productKey, CancellationToken cancellationToken);

    Task<LicenseActionResultDto> DeactivateAsync(CancellationToken cancellationToken);

    /// <summary>Checks in with the license server now instead of waiting for the next scheduled check.</summary>
    Task<LicenseStatusDto> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Keeps this computer's license: activates it, checks in with the licensing site on the interval the server asks for,
/// and keeps the service working offline on the last signed token until its offline period ends.
/// </summary>
public sealed class LicenseService : BackgroundService, ILicenseState
{
    private const string IssueKey = LicenseCodes.IssueId;
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromHours(1);
    private static readonly TimeSpan KeyRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StatusCacheTime = TimeSpan.FromSeconds(5);

    private readonly ILicensePlatformClient _client;
    private readonly ILicenseStore _store;
    private readonly IMonitorHealthStore _health;
    private readonly LicensingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LicenseService> _logger;
    private readonly string _deviceId;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _sync = new();

    private StoredLicense _stored;
    private SigningKeySet _keys;
    private LicenseClaims? _claims;
    private string? _tokenError;
    private DateTime? _acceptedUtc;
    private bool _lastCheckFailed;
    private string? _lastCheckMessage;
    private TimeSpan _checkInterval;
    private DateTime? _nextCheckUtc;
    private DateTime _keysFetchedUtc = DateTime.MinValue;
    private int _failures;
    private LicenseStatusDto _status = new();
    private DateTime _evaluatedUtc = DateTime.MinValue;
    private string? _publishedIssue;

    public LicenseService(
        ILicensePlatformClient client,
        ILicenseStore store,
        IMonitorHealthStore health,
        IOptions<LicensingOptions> options,
        TimeProvider time,
        ILogger<LicenseService> logger)
    {
        _client = client;
        _store = store;
        _health = health;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _deviceId = DeviceFingerprint.Get(_options.ProductCode);
        _checkInterval = TimeSpan.FromHours(Math.Max(1, _options.DefaultCheckHours));
        _stored = store.Load();
        _keys = SigningKeySet.Parse(_stored.SigningKeysJson);
        VerifyStoredToken();
        Publish();
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public bool IsLicensed => Current().IsValid;

    public LicenseStatusDto GetStatus() => Current();

    public async Task<LicenseActionResultDto> ActivateAsync(string productKey, CancellationToken cancellationToken)
    {
        var key = new string((productKey ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (key.Length < 8)
        {
            return Result(false, "Enter the product key you received with your license.");
        }

        await _operation.WaitAsync(cancellationToken);
        try
        {
            var reply = await _client.ActivateAsync(key, _deviceId, cancellationToken);
            switch (reply.Kind)
            {
                case LicenseReplyKind.Accepted:
                    await ApplyAcceptedAsync(reply, key, replaceToken: true, cancellationToken);
                    _logger.LogInformation("[License] Activated {Prefix} on device {DeviceId}", Prefix(key), _deviceId);
                    return Result(true, "MonitorAgent is activated on this computer.");
                case LicenseReplyKind.Rejected:
                    _logger.LogWarning("[License] Activation of {Prefix} refused: {Code} {Message}", Prefix(key), reply.ErrorCode, reply.Message);
                    return Result(false, Describe(reply));
                default:
                    _logger.LogWarning("[License] Activation of {Prefix} not possible now: {Message}", Prefix(key), reply.Message);
                    return Result(false, reply.Message);
            }
        }
        finally
        {
            _operation.Release();
            Wake();
        }
    }

    public async Task<LicenseActionResultDto> DeactivateAsync(CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (LicenseStore.ProductKey(_stored) is not { } key)
            {
                Clear();
                return Result(true, "No license is active on this computer.");
            }

            var reply = await _client.DeactivateAsync(key, _deviceId, cancellationToken);
            if (reply.Kind == LicenseReplyKind.Unavailable)
            {
                return Result(false, reply.Message);
            }

            Clear();
            _logger.LogInformation("[License] Released {Prefix} from device {DeviceId}", Prefix(key), _deviceId);
            return Result(true, "The license was released from this computer and can be activated on another one.");
        }
        finally
        {
            _operation.Release();
            Wake();
        }
    }

    public async Task<LicenseStatusDto> RefreshAsync(CancellationToken cancellationToken)
    {
        await CheckAsync(heartbeat: true, cancellationToken);
        Wake();
        return Current();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[License] Device id {DeviceId}; licensing site {Url}", _deviceId, _options.PlatformUrl);
        var heartbeat = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (LicenseStore.ProductKey(_stored) is not null && (_nextCheckUtc is null || Now >= _nextCheckUtc))
            {
                try
                {
                    await CheckAsync(heartbeat, stoppingToken);
                    heartbeat = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[License] License check failed");
                    _nextCheckUtc = Now + FirstRetry;
                }
            }

            var delay = LicenseStore.ProductKey(_stored) is not null && _nextCheckUtc is { } due
                ? due - Now
                : TimeSpan.FromHours(1);
            try
            {
                await _wake.WaitAsync(delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            Publish();
        }
    }

    private async Task CheckAsync(bool heartbeat, CancellationToken cancellationToken)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            if (LicenseStore.ProductKey(_stored) is not { } key)
            {
                Publish();
                return;
            }

            var reply = await _client.CheckAsync(key, _deviceId, heartbeat, cancellationToken);
            switch (reply.Kind)
            {
                case LicenseReplyKind.Accepted:
                    await ApplyAcceptedAsync(reply, key, replaceToken: false, cancellationToken);
                    break;
                case LicenseReplyKind.Rejected:
                    ApplyRejected(reply);
                    break;
                default:
                    ApplyUnavailable(reply);
                    break;
            }
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task ApplyAcceptedAsync(LicenseReply reply, string productKey, bool replaceToken, CancellationToken cancellationToken)
    {
        if (reply.Token is { } token)
        {
            await EnsureSigningKeysAsync(token, cancellationToken);
        }

        var now = Now;
        _stored = _stored with
        {
            ProtectedProductKey = SecretProtector.Protect(productKey),
            KeyPrefix = Prefix(productKey),
            Token = replaceToken ? reply.Token : reply.Token ?? _stored.Token,
            LastOnlineUtc = now,
            LastSeenUtc = Later(_stored.LastSeenUtc, now),
            RejectedState = null,
            RejectedCode = null,
            RejectedMessage = null
        };
        VerifyStoredToken();
        if (_tokenError is not null)
        {
            _logger.LogWarning("[License] The server accepted the license, but its token cannot be used offline: {Error}", _tokenError);
        }

        _acceptedUtc = now;
        _lastCheckFailed = false;
        _lastCheckMessage = null;
        _failures = 0;
        var seconds = reply.NextCheckAfterSeconds is > 0 ? reply.NextCheckAfterSeconds.Value : _options.DefaultCheckHours * 3600;
        _checkInterval = TimeSpan.FromSeconds(Math.Max(MinCheckInterval.TotalSeconds, seconds));
        _nextCheckUtc = now + Jitter(_checkInterval);
        _store.Save(_stored);
        Publish();
    }

    private void ApplyRejected(LicenseReply reply)
    {
        var state = reply.ErrorCode?.ToUpperInvariant() switch
        {
            "LIC_LICENSE_EXPIRED" or "LIC_SUBSCRIPTION_EXPIRED" => LicenseState.Expired,
            "LIC_LICENSE_SUSPENDED" => LicenseState.Suspended,
            "LIC_LICENSE_REVOKED" => LicenseState.Revoked,
            _ => LicenseState.Invalid
        };
        var dropKey = state == LicenseState.Revoked || string.Equals(reply.ErrorCode, "LIC_INVALID_LICENSE", StringComparison.OrdinalIgnoreCase);
        _logger.LogWarning("[License] The license server refused the license: {Code} {Message}", reply.ErrorCode, reply.Message);
        _stored = _stored with
        {
            ProtectedProductKey = dropKey ? null : _stored.ProtectedProductKey,
            Token = null,
            LastSeenUtc = Later(_stored.LastSeenUtc, Now),
            RejectedState = state,
            RejectedCode = reply.ErrorCode,
            RejectedMessage = Describe(reply)
        };
        _claims = null;
        _acceptedUtc = null;
        _lastCheckFailed = false;
        _failures = 0;
        // A suspended or expired license can be resumed or renewed on the server, so it is asked about again later.
        _nextCheckUtc = dropKey ? null : Now + Jitter(_checkInterval);
        _store.Save(_stored);
        Publish();
    }

    private void ApplyUnavailable(LicenseReply reply)
    {
        _failures++;
        _lastCheckFailed = true;
        _lastCheckMessage = reply.Message;
        var backoff = TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, FirstRetry.Ticks << Math.Min(_failures - 1, 6)));
        if (reply.RetryAfter is { } retryAfter && retryAfter > backoff)
        {
            backoff = retryAfter;
        }

        _nextCheckUtc = Now + Jitter(backoff);
        _stored = _stored with { LastSeenUtc = Later(_stored.LastSeenUtc, Now) };
        _store.Save(_stored);
        _logger.LogWarning("[License] {Message}; next try at {Next:HH:mm}", reply.Message, _nextCheckUtc.Value.ToLocalTime());
        Publish();
    }

    private void Clear()
    {
        _stored = new StoredLicense { SigningKeysJson = _stored.SigningKeysJson, LastSeenUtc = Later(_stored.LastSeenUtc, Now) };
        _claims = null;
        _tokenError = null;
        _acceptedUtc = null;
        _lastCheckFailed = false;
        _nextCheckUtc = null;
        _store.Save(_stored);
        Publish();
    }

    private async Task EnsureSigningKeysAsync(string token, CancellationToken cancellationToken)
    {
        if (_keys.Contains(LicenseToken.ReadKeyId(token)) || Now - _keysFetchedUtc < KeyRefreshInterval)
        {
            return;
        }

        _keysFetchedUtc = Now;
        if (await _client.GetSigningKeysAsync(cancellationToken) is { } json && SigningKeySet.Parse(json) is { Count: > 0 } keys)
        {
            _keys = keys;
            _stored = _stored with { SigningKeysJson = json };
            _logger.LogInformation("[License] Downloaded {Count} signing keys", keys.Count);
        }
    }

    private void VerifyStoredToken()
    {
        _claims = null;
        _tokenError = null;
        if (_stored.Token is null)
        {
            return;
        }

        var claims = LicenseToken.Verify(_stored.Token, _keys, out var error);
        if (claims is null)
        {
            _tokenError = error;
        }
        else if (claims.ProductCode is { } product && !product.Equals(_options.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            _tokenError = $"The license token is for another product ({product}).";
        }
        else if (claims.DeviceId is { } device && device != _deviceId)
        {
            _tokenError = "The license token belongs to another computer.";
        }
        else
        {
            _claims = claims;
        }
    }

    private LicenseStatusDto Current()
    {
        lock (_sync)
        {
            if (Now - _evaluatedUtc > StatusCacheTime)
            {
                Publish();
            }

            return _status;
        }
    }

    /// <summary>Works out the state again and shows or clears the license problem in Messages &amp; Issues when it changed.</summary>
    private void Publish()
    {
        lock (_sync)
        {
            var status = Evaluate();
            _status = status;
            _evaluatedUtc = Now;
            var signature = status.IsValid ? null : $"{status.State}|{status.Message}";
            if (signature == _publishedIssue)
            {
                return;
            }

            _publishedIssue = signature;
            if (signature is null)
            {
                _health.ClearIssue(IssueKey);
            }
            else
            {
                _health.SetIssue(IssueKey, "Critical", "License required",
                    $"{status.Message} Monitoring continues in the background; results are shown once the license is active.");
            }
        }
    }

    private LicenseStatusDto Evaluate()
    {
        var now = Now;
        var offlineUntil = OfflineUntil();
        LicenseStatusDto Build(LicenseState state, bool valid, string message) => new()
        {
            State = state,
            IsValid = valid,
            Message = message,
            ErrorCode = _stored.RejectedCode,
            LicenseNumber = _claims?.LicenseNumber ?? _claims?.LicenseId,
            KeyPrefix = _stored.KeyPrefix,
            DeviceId = _deviceId,
            ExpiresAtUtc = _claims?.LicenseExpiresAtUtc,
            LastOnlineUtc = _stored.LastOnlineUtc,
            OfflineUntilUtc = offlineUntil,
            NextCheckUtc = _nextCheckUtc,
            Features = _claims?.Features ?? [],
            Limits = _claims?.Limits ?? new Dictionary<string, long?>()
        };

        var hasKey = LicenseStore.ProductKey(_stored) is not null;
        if (_stored.RejectedState is { } rejected && (_stored.Token is null || !hasKey))
        {
            return Build(rejected, false, _stored.RejectedMessage ?? $"The license is {rejected.ToString().ToLowerInvariant()}.");
        }

        if (!hasKey)
        {
            return Build(LicenseState.NotActivated, false, "Enter a product key to activate MonitorAgent on this computer.");
        }

        if (_stored.LastSeenUtc is { } seen && now < seen - ClockTolerance)
        {
            return Build(LicenseState.Invalid, false, "This computer's clock is earlier than the last license check. Correct the date and time.");
        }

        if (_claims?.LicenseExpiresAtUtc is { } end && now >= end)
        {
            return Build(LicenseState.Expired, false, $"The license ended on {end.ToLocalTime():yyyy-MM-dd}. Renew it to continue.");
        }

        var acceptedRecently = _acceptedUtc is { } accepted && now - accepted < _checkInterval + TimeSpan.FromHours(1);
        if (!_lastCheckFailed && acceptedRecently)
        {
            return Build(LicenseState.Active, true, "The license is active.");
        }

        if (offlineUntil is { } until && now < until)
        {
            return _lastCheckFailed
                ? Build(LicenseState.Offline, true, $"The license server cannot be reached; MonitorAgent keeps working until {until.ToLocalTime():yyyy-MM-dd HH:mm}.")
                : Build(LicenseState.Active, true, "The license is active.");
        }

        if (acceptedRecently)
        {
            return Build(LicenseState.Offline, true, "The license server cannot be reached right now.");
        }

        if (offlineUntil is not null)
        {
            var since = _stored.LastOnlineUtc is { } online ? $" since {online.ToLocalTime():yyyy-MM-dd HH:mm}" : string.Empty;
            return Build(LicenseState.Offline, false, $"The license server has not been reached{since}. Connect this computer to the internet to continue.");
        }

        return Build(LicenseState.Invalid, false, _lastCheckMessage ?? _tokenError ?? "The license has not been confirmed by the license server yet.");
    }

    /// <summary>
    /// The end of the offline period: the token's own expiry, but never later than the plan's offline days after the token
    /// was issued. Only signed values are used, so editing license.json cannot extend it.
    /// </summary>
    private DateTime? OfflineUntil()
    {
        if (_claims is null)
        {
            return null;
        }

        var graceDays = Math.Max(0, _claims.OfflineGraceDays ?? _options.OfflineGraceDays);
        DateTime? byGrace = _claims.IssuedAtUtc is { } issued ? issued.AddDays(graceDays) : null;
        var limits = new[] { byGrace, _claims.TokenExpiresAtUtc, _claims.LicenseExpiresAtUtc }.Where(d => d is not null).Select(d => d!.Value).ToList();
        return limits.Count == 0 ? null : limits.Min();
    }

    private LicenseActionResultDto Result(bool success, string message)
    {
        lock (_sync)
        {
            Publish();
            return new LicenseActionResultDto(success, message, _status);
        }
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static string Describe(LicenseReply reply) => reply.ErrorCode?.ToUpperInvariant() switch
    {
        "LIC_INVALID_LICENSE" => "The product key is not valid for MonitorAgent. Check the key and try again.",
        "LIC_NOT_STARTED" => "The license has not started yet.",
        "LIC_LICENSE_EXPIRED" => "The license has expired. Renew it to continue.",
        "LIC_SUBSCRIPTION_EXPIRED" => "The subscription for this license has ended. Renew it to continue.",
        "LIC_LICENSE_SUSPENDED" => "The license is suspended. Contact your provider.",
        "LIC_LICENSE_REVOKED" => "The license was revoked. Enter a new product key.",
        "LIC_DEVICE_NOT_ACTIVATED" => "This computer is no longer activated on the licensing site. Enter the product key again.",
        "LIC_ACTIVATION_LIMIT_REACHED" => "This license is already active on the maximum number of computers. Release it on another computer first.",
        "VALIDATION_FAILED" => "The product key is not in the expected format.",
        _ => reply.Message
    };

    private static string Prefix(string key)
    {
        var first = key.Split('-')[0];
        return (first.Length is > 0 and <= 8 ? first : key[..Math.Min(4, key.Length)]) + "-…";
    }

    private static DateTime Later(DateTime? a, DateTime b) => a is { } value && value > b ? value : b;

    private static TimeSpan Jitter(TimeSpan interval) => interval * (0.9 + Random.Shared.NextDouble() * 0.2);
}
