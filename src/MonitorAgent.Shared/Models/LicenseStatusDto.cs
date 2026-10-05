namespace MonitorAgent.Shared.Models;

public enum LicenseState
{
    NotActivated,
    Active,
    /// <summary>The license server could not be reached; the last signed token is used until the offline period ends.</summary>
    Offline,
    Expired,
    Suspended,
    Revoked,
    Invalid
}

public sealed class LicenseStatusDto
{
    public LicenseState State { get; init; }

    /// <summary>True when results may be shown: the license is active, or offline within its allowed period.</summary>
    public bool IsValid { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>The platform's error code of the last refusal, such as LIC_LICENSE_EXPIRED.</summary>
    public string? ErrorCode { get; init; }

    public string? LicenseNumber { get; init; }

    /// <summary>The first group of the product key; the full key is never returned.</summary>
    public string? KeyPrefix { get; init; }

    public string DeviceId { get; init; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; init; }

    public DateTime? LastOnlineUtc { get; init; }

    public DateTime? OfflineUntilUtc { get; init; }

    public DateTime? NextCheckUtc { get; init; }

    public List<string> Features { get; init; } = [];

    public Dictionary<string, long?> Limits { get; init; } = new();
}

public static class LicenseCodes
{
    /// <summary>The "code" of the 403 answer the service gives for results while it has no valid license.</summary>
    public const string Required = "LICENSE_REQUIRED";

    /// <summary>The id of the license notice in the issues and notifications lists.</summary>
    public const string IssueId = "license";
}

public sealed record LicenseActivateRequest(string ProductKey);

public sealed record LicenseActionResultDto(bool Success, string Message, LicenseStatusDto Status);
