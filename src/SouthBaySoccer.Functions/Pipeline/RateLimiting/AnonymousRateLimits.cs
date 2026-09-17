using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Functions.Pipeline.RateLimiting;

/// <summary>
/// Rules and key derivation for the anonymous onboarding endpoints. Keys are SHA-256 hashes so no
/// raw IP, phone number, or email is held in memory or could reach a log through a key.
/// </summary>
public static class AnonymousRateLimits
{
    /// <summary>Every anonymous onboarding call, per caller IP.</summary>
    public static readonly RateLimitRule PerIp = new("onboarding-ip", 30, TimeSpan.FromMinutes(5));

    /// <summary>Phone sign-in starts per phone number, so one number cannot be hammered from many IPs.</summary>
    public static readonly RateLimitRule PerPhone = new("signin-phone", 5, TimeSpan.FromMinutes(15));

    /// <summary>Email pre-flight checks per email, so the endpoint cannot enumerate addresses.</summary>
    public static readonly RateLimitRule PerEmail = new("register-email", 10, TimeSpan.FromMinutes(15));

    /// <summary>Derives the caller-IP key from the proxy headers on the request.</summary>
    public static string ClientIpKey(HttpRequestData request) =>
        ClientIpKey(FirstHeaderValue(request, "X-Azure-ClientIP"), FirstHeaderValue(request, "X-Forwarded-For"));

    /// <summary>
    /// Derives the caller-IP key. Azure Front Door / App Service stamp <c>X-Azure-ClientIP</c> with
    /// the connecting address and it wins; otherwise the <b>last</b> <c>X-Forwarded-For</c> entry is
    /// the one appended by our own edge (earlier entries are client-supplied and spoofable). Ports
    /// are stripped so <c>1.2.3.4:5678</c> and <c>[2001:db8::1]:5678</c> share their address's bucket.
    /// Unknown callers share one bucket.
    /// </summary>
    public static string ClientIpKey(string? azureClientIp, string? forwardedFor)
    {
        var address = NormalizeAddress(azureClientIp);
        if (address is null && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var hops = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            address = hops.Length == 0 ? null : NormalizeAddress(hops[^1]);
        }

        return Hash(address ?? "unknown");
    }

    /// <summary>Derives the per-phone key from the same normalized digits the Pickup Pal lookup uses.</summary>
    public static string PhoneKey(string phoneNumber) =>
        Hash("+" + BeginPhoneSignInCommandValidator.NormalizeDigits(phoneNumber));

    /// <summary>Derives a key from user-typed text (email) after trimming and lower-casing.</summary>
    public static string InputKey(string value) => Hash(value.Trim().ToLowerInvariant());

    private static string? NormalizeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('['))
        {
            // Bracketed IPv6, optionally with a port: [addr]:port
            var close = candidate.IndexOf(']');
            return close > 1 ? candidate[1..close] : null;
        }

        // IPv4 with a port has exactly one colon; a bare IPv6 address has several and no port.
        var colonCount = candidate.Count(character => character == ':');
        return colonCount == 1 ? candidate[..candidate.IndexOf(':')] : candidate;
    }

    private static string? FirstHeaderValue(HttpRequestData request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
