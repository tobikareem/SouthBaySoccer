using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Onboarding;

namespace SouthBaySoccer.Infrastructure.Authentication.Onboarding;

/// <summary>
/// <see cref="IOnboardingPolicy"/> backed by <see cref="OnboardingOptions"/>. Exempt numbers are
/// normalized once to <c>+digits</c> and compared transiently; nothing is persisted or logged.
/// </summary>
public sealed class ConfiguredOnboardingPolicy : IOnboardingPolicy
{
    private readonly OnboardingOptions options;
    private readonly HashSet<string> exemptPhoneNumbers;

    /// <summary>Initializes a new instance of the <see cref="ConfiguredOnboardingPolicy"/> class.</summary>
    /// <param name="options">The bound onboarding options.</param>
    public ConfiguredOnboardingPolicy(IOptions<OnboardingOptions> options)
    {
        this.options = options.Value;
        exemptPhoneNumbers = this.options.VerificationExemptPhoneNumbers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(phoneNumber => phoneNumber is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public bool RequireWhatsAppVerification => options.RequireWhatsAppVerification;

    /// <inheritdoc />
    public TimeSpan PendingSignInLifetime => options.PendingSignInLifetime;

    /// <inheritdoc />
    public TimeSpan RememberDeviceRefreshTokenLifetime => options.RememberDeviceRefreshTokenLifetime;

    /// <inheritdoc />
    public string TermsVersion => options.TermsVersion;

    /// <inheritdoc />
    public bool IsVerificationExempt(string phoneNumber)
    {
        var normalized = Normalize(phoneNumber);
        return normalized is not null && exemptPhoneNumbers.Contains(normalized);
    }

    private static string? Normalize(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : $"+{digits}";
    }
}
