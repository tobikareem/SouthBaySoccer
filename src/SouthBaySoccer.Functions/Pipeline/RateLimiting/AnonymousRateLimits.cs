using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;

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

    /// <summary>Derives the caller-IP key from proxy headers; unknown callers share one bucket.</summary>
    public static string ClientIpKey(HttpRequestData request)
    {
        var address = FirstHeaderValue(request, "X-Forwarded-For")?.Split(',')[0].Trim()
            ?? FirstHeaderValue(request, "X-Azure-ClientIP")?.Trim();

        return Hash(string.IsNullOrWhiteSpace(address) ? "unknown" : address);
    }

    /// <summary>Derives a key from user-typed input (phone or email) after trimming and lower-casing.</summary>
    public static string InputKey(string value) => Hash(value.Trim().ToLowerInvariant());

    private static string? FirstHeaderValue(HttpRequestData request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
