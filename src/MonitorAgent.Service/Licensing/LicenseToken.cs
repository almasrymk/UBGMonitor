using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonitorAgent.Service.Licensing;

/// <summary>What a verified license token says. Only values from the signed payload end up here.</summary>
public sealed record LicenseClaims
{
    public string? LicenseId { get; init; }
    public string? LicenseNumber { get; init; }
    public string? ProductCode { get; init; }
    public string? DeviceId { get; init; }
    public string? Status { get; init; }
    public DateTime? IssuedAtUtc { get; init; }

    /// <summary>When the token itself stops being accepted offline.</summary>
    public DateTime? TokenExpiresAtUtc { get; init; }

    /// <summary>When the license ends; null for a lifetime license.</summary>
    public DateTime? LicenseExpiresAtUtc { get; init; }

    public DateTime? CheckAfterUtc { get; init; }
    public int? OfflineGraceDays { get; init; }
    public List<string> Features { get; init; } = [];
    public Dictionary<string, long?> Limits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The platform's public signing keys (GET /api/v1/signing-keys), selected by kid.</summary>
public sealed class SigningKeySet
{
    private readonly Dictionary<string, ECParameters> _keys;

    private SigningKeySet(Dictionary<string, ECParameters> keys) => _keys = keys;

    public static SigningKeySet Empty { get; } = new(new Dictionary<string, ECParameters>(StringComparer.Ordinal));

    public int Count => _keys.Count;

    public bool Contains(string? kid) => kid is not null && _keys.ContainsKey(kid);

    public bool TryGet(string kid, out ECParameters parameters) => _keys.TryGetValue(kid, out parameters);

    /// <summary>Reads the JWK list; keys that are not P-256 EC keys are skipped.</summary>
    public static SigningKeySet Parse(string? json)
    {
        var keys = new Dictionary<string, ECParameters>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SigningKeySet(keys);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("keys", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return new SigningKeySet(keys);
            }

            foreach (var key in list.EnumerateArray())
            {
                if (Text(key, "kty") != "EC" || Text(key, "crv") != "P-256"
                    || Text(key, "kid") is not { } kid || Text(key, "x") is not { } x || Text(key, "y") is not { } y)
                {
                    continue;
                }

                keys[kid] = new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = Base64Url.Decode(x), Y = Base64Url.Decode(y) }
                };
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
        }

        return new SigningKeySet(keys);
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public static class LicenseToken
{
    /// <summary>The key id in the token header, to know whether the signing keys must be downloaded again.</summary>
    public static string? ReadKeyId(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64Url.Decode(parts[0]));
            return header.RootElement.TryGetProperty("kid", out var kid) && kid.ValueKind == JsonValueKind.String ? kid.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Checks the ES256 signature and returns the claims; null with the reason when the token cannot be trusted.</summary>
    public static LicenseClaims? Verify(string? token, SigningKeySet keys, out string? error)
    {
        error = null;
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            error = "The license token is not a signed token.";
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64Url.Decode(parts[0]));
            var alg = Claims.Text(header.RootElement, "alg");
            var kid = Claims.Text(header.RootElement, "kid");
            if (alg != "ES256")
            {
                error = $"The license token is signed with {alg ?? "no algorithm"}, not ES256.";
                return null;
            }

            if (kid is null || !keys.TryGet(kid, out var parameters))
            {
                error = $"The license token was signed with an unknown key ({kid ?? "no key id"}).";
                return null;
            }

            using var ecdsa = ECDsa.Create(parameters);
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (!ecdsa.VerifyData(signed, Base64Url.Decode(parts[2]), HashAlgorithmName.SHA256))
            {
                error = "The license token signature is not valid.";
                return null;
            }

            using var payload = JsonDocument.Parse(Base64Url.Decode(parts[1]));
            return Claims.Read(payload.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
        {
            error = $"The license token could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>Reads the payload with the claim names the platform may use (snake_case, camelCase or short JWT names).</summary>
    private static class Claims
    {
        public static LicenseClaims Read(JsonElement root) => new()
        {
            LicenseId = Text(root, "license_id", "licenseId", "lid", "sub"),
            LicenseNumber = Text(root, "license_number", "licenseNumber", "lic"),
            ProductCode = Text(root, "product_code", "productCode", "prd", "product"),
            DeviceId = Text(root, "device_id", "deviceId", "did", "device"),
            Status = Text(root, "status", "license_status", "licenseStatus"),
            IssuedAtUtc = Time(root, "iat", "issued_at", "issuedAt"),
            TokenExpiresAtUtc = Time(root, "exp"),
            LicenseExpiresAtUtc = Time(root, "license_expires_at", "licenseExpiresAt", "expires_at", "expiresAt"),
            CheckAfterUtc = Time(root, "check_after", "checkAfter"),
            OfflineGraceDays = Number(root, "offline_grace_days", "offlineGraceDays", "grace_days") is { } days ? (int)days : null,
            Features = Features(root),
            Limits = Limits(root)
        };

        public static string? Text(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.TryGetProperty(name, out var value))
                {
                    switch (value.ValueKind)
                    {
                        case JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()):
                            return value.GetString();
                        case JsonValueKind.Number:
                            return value.GetRawText();
                    }
                }
            }

            return null;
        }

        private static double? Number(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (!root.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetDouble();
                }

                if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        /// <summary>Unix seconds or an ISO-8601 date.</summary>
        private static DateTime? Time(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (!root.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                }

                if (value.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed.UtcDateTime;
                }
            }

            return null;
        }

        private static List<string> Features(JsonElement root)
        {
            foreach (var name in new[] { "features", "feature" })
            {
                if (!root.TryGetProperty(name, out var value))
                {
                    continue;
                }

                var list = Unwrap(value);
                return list.ValueKind switch
                {
                    JsonValueKind.Array => list.EnumerateArray()
                        .Where(f => f.ValueKind == JsonValueKind.String)
                        .Select(f => f.GetString()!)
                        .ToList(),
                    JsonValueKind.String => [list.GetString()!],
                    _ => []
                };
            }

            return [];
        }

        private static Dictionary<string, long?> Limits(JsonElement root)
        {
            var limits = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("limits", out var value) || Unwrap(value) is not { ValueKind: JsonValueKind.Object } map)
            {
                return limits;
            }

            foreach (var limit in map.EnumerateObject())
            {
                limits[limit.Name] = limit.Value.ValueKind switch
                {
                    JsonValueKind.Number when limit.Value.TryGetInt64(out var n) => n,
                    JsonValueKind.String when long.TryParse(limit.Value.GetString(), out var n) => n,
                    _ => null
                };
            }

            return limits;
        }

        /// <summary>JWT libraries often put arrays and objects in a claim as JSON text.</summary>
        private static JsonElement Unwrap(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text
                && (text.StartsWith('[') || text.StartsWith('{')))
            {
                try
                {
                    using var inner = JsonDocument.Parse(text);
                    return inner.RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }

            return value;
        }
    }
}

public static class Base64Url
{
    public static byte[] Decode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
    }

    public static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
