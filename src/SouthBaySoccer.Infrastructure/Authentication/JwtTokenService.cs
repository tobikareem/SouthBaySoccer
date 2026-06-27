using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// HMAC-SHA256 JWT access-token service for SouthBaySoccer application sessions.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private const string Algorithm = "HS256";
    private const string TokenType = "JWT";
    private const string RoleClaim = "role";
    private const string PolicyClaim = "policy";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly JwtTokenOptions options;
    private readonly IClock clock;
    private readonly Dictionary<string, JwtSigningKeyOptions> signingKeys;
    private readonly JwtSigningKeyOptions activeSigningKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtTokenService"/> class.
    /// </summary>
    /// <param name="options">The configured JWT token options.</param>
    /// <param name="clock">The UTC clock.</param>
    public JwtTokenService(IOptions<JwtTokenOptions> options, IClock clock)
    {
        this.options = options.Value;
        this.clock = clock;
        signingKeys = BuildSigningKeyIndex(this.options.SigningKeys);
        activeSigningKey = ResolveActiveSigningKey(this.options, signingKeys);
        ValidateOptions(this.options);
    }

    /// <inheritdoc />
    public IssuedAccessToken IssueAccessToken(AccessTokenIssueRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("Access tokens require a non-empty user id.", nameof(request));
        }

        var issuedAtUtc = EnsureUtc(clock.UtcNow);
        var expiresAtUtc = issuedAtUtc.Add(options.AccessTokenLifetime);
        var header = new Dictionary<string, object>
        {
            ["alg"] = Algorithm,
            ["typ"] = TokenType,
            ["kid"] = activeSigningKey.KeyId,
        };
        var payload = new Dictionary<string, object>
        {
            ["iss"] = options.Issuer,
            ["aud"] = options.Audience,
            ["sub"] = request.UserId.ToString("D"),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = ToUnixTimeSeconds(issuedAtUtc),
            ["nbf"] = ToUnixTimeSeconds(issuedAtUtc),
            ["exp"] = ToUnixTimeSeconds(expiresAtUtc),
            [RoleClaim] = NormalizeClaims(request.Roles),
            [PolicyClaim] = NormalizeClaims(request.Policies),
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var unsignedToken = $"{encodedHeader}.{encodedPayload}";
        var signature = Sign(unsignedToken, activeSigningKey.Secret);

        return new IssuedAccessToken($"{unsignedToken}.{signature}", expiresAtUtc, activeSigningKey.KeyId);
    }

    /// <inheritdoc />
    public AccessTokenValidationResult ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AccessTokenValidationResult.Failed("missing_token");
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return AccessTokenValidationResult.Failed("malformed_token");
        }

        JsonDocument headerDocument;
        try
        {
            headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        }
        catch (JsonException)
        {
            return AccessTokenValidationResult.Failed("malformed_header");
        }
        catch (FormatException)
        {
            return AccessTokenValidationResult.Failed("malformed_header");
        }

        using (headerDocument)
        {
            if (!TryGetString(headerDocument.RootElement, "alg", out var algorithm) || algorithm != Algorithm)
            {
                return AccessTokenValidationResult.Failed("unsupported_algorithm");
            }

            if (!TryGetString(headerDocument.RootElement, "kid", out var keyId) || !signingKeys.TryGetValue(keyId, out var signingKey))
            {
                return AccessTokenValidationResult.Failed("unknown_key");
            }

            var unsignedToken = $"{parts[0]}.{parts[1]}";
            if (!IsSignatureValid(unsignedToken, parts[2], signingKey.Secret))
            {
                return AccessTokenValidationResult.Failed("invalid_signature");
            }

            return ValidatePayload(parts[1], keyId);
        }
    }

    private AccessTokenValidationResult ValidatePayload(string encodedPayload, string keyId)
    {
        JsonDocument payloadDocument;
        try
        {
            payloadDocument = JsonDocument.Parse(Base64UrlDecode(encodedPayload));
        }
        catch (JsonException)
        {
            return AccessTokenValidationResult.Failed("malformed_payload");
        }
        catch (FormatException)
        {
            return AccessTokenValidationResult.Failed("malformed_payload");
        }

        using (payloadDocument)
        {
            var payload = payloadDocument.RootElement;
            if (!TryGetString(payload, "iss", out var issuer) || issuer != options.Issuer)
            {
                return AccessTokenValidationResult.Failed("invalid_issuer");
            }

            if (!TryGetString(payload, "aud", out var audience) || audience != options.Audience)
            {
                return AccessTokenValidationResult.Failed("invalid_audience");
            }

            if (!TryGetString(payload, "sub", out var subject) || !Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
            {
                return AccessTokenValidationResult.Failed("invalid_subject");
            }

            if (!TryGetInt64(payload, "exp", out var expiresAtUnix))
            {
                return AccessTokenValidationResult.Failed("missing_expiration");
            }

            var nowUtc = EnsureUtc(clock.UtcNow);
            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).UtcDateTime;
            if (nowUtc >= expiresAtUtc)
            {
                return AccessTokenValidationResult.Failed("expired_token");
            }

            if (TryGetInt64(payload, "nbf", out var notBeforeUnix))
            {
                var notBeforeUtc = DateTimeOffset.FromUnixTimeSeconds(notBeforeUnix).UtcDateTime;
                if (nowUtc < notBeforeUtc)
                {
                    return AccessTokenValidationResult.Failed("token_not_yet_valid");
                }
            }

            var roles = ReadStringCollection(payload, RoleClaim);
            var policies = ReadStringCollection(payload, PolicyClaim);
            return AccessTokenValidationResult.Succeeded(userId, roles, policies, expiresAtUtc, keyId);
        }
    }

    private static Dictionary<string, JwtSigningKeyOptions> BuildSigningKeyIndex(IEnumerable<JwtSigningKeyOptions> keys)
    {
        var index = new Dictionary<string, JwtSigningKeyOptions>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.KeyId))
            {
                throw new InvalidOperationException("JWT signing keys require a key id.");
            }

            if (string.IsNullOrWhiteSpace(key.Secret))
            {
                throw new InvalidOperationException("JWT signing keys require secret material.");
            }

            if (!index.TryAdd(key.KeyId, key))
            {
                throw new InvalidOperationException($"JWT signing key id '{key.KeyId}' is duplicated.");
            }
        }

        return index;
    }

    private static JwtSigningKeyOptions ResolveActiveSigningKey(
        JwtTokenOptions options,
        IReadOnlyDictionary<string, JwtSigningKeyOptions> signingKeys)
    {
        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            throw new InvalidOperationException("JWT options require an active key id.");
        }

        if (!signingKeys.TryGetValue(options.ActiveKeyId, out var activeKey))
        {
            throw new InvalidOperationException("The active JWT signing key id must be present in the validation key set.");
        }

        return activeKey;
    }

    private static void ValidateOptions(JwtTokenOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("JWT options require an issuer.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("JWT options require an audience.");
        }

        if (options.AccessTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("JWT access-token lifetime must be positive.");
        }
    }

    private static IReadOnlyList<string> NormalizeClaims(IReadOnlyList<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ReadStringCollection(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property))
        {
            return Array.Empty<string>();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() is { Length: > 0 } value
                ? [value]
                : Array.Empty<string>();
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out value);
    }

    private static string Sign(string unsignedToken, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsignedToken)));
    }

    private static bool IsSignatureValid(string unsignedToken, string signature, string secret)
    {
        string expectedSignature;
        try
        {
            expectedSignature = Sign(unsignedToken, secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedBytes = Encoding.ASCII.GetBytes(expectedSignature);
        var suppliedBytes = Encoding.ASCII.GetBytes(signature);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static long ToUnixTimeSeconds(DateTime utcDateTime) =>
        new DateTimeOffset(EnsureUtc(utcDateTime)).ToUnixTimeSeconds();

    private static DateTime EnsureUtc(DateTime dateTime) =>
        dateTime.Kind == DateTimeKind.Utc
            ? dateTime
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
}
