using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Resolves configured game-admin phone numbers.
/// </summary>
public sealed class ConfiguredAdminPhoneNumberService : IConfiguredAdminPhoneNumberService
{
    private readonly HashSet<string> normalizedPhoneNumbers;
    private readonly HashSet<string> normalizedPhoneNumberHashes;

    public ConfiguredAdminPhoneNumberService(IOptions<AdminPhoneNumberOptions> options)
    {
        var configuredNumbers = SplitPhoneNumbers(options.Value.AdminPhoneNumbers);
        normalizedPhoneNumbers = configuredNumbers.ToHashSet(StringComparer.Ordinal);
        normalizedPhoneNumberHashes = configuredNumbers
            .Select(AuthenticationHashing.Sha256)
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool IsConfiguredAdminPhoneNumber(string phoneNumber)
    {
        var normalized = NormalizePhoneNumber(phoneNumber);
        return normalized is not null && normalizedPhoneNumbers.Contains(normalized);
    }

    public bool IsConfiguredAdminPhoneNumberHash(string? phoneNumberHash) =>
        !string.IsNullOrWhiteSpace(phoneNumberHash) &&
        normalizedPhoneNumberHashes.Contains(phoneNumberHash.Trim());

    private static IEnumerable<string> SplitPhoneNumbers(string configuredPhoneNumbers) =>
        configuredPhoneNumbers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePhoneNumber)
            .Where(phoneNumber => phoneNumber is not null)
            .Cast<string>();

    private static string? NormalizePhoneNumber(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : $"+{digits}";
    }
}
