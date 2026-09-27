using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Resolves configured game-admin and super-admin (owner) phone numbers. Both lists are normalized
/// to <c>+digits</c> and compared either transiently against a raw sign-in phone or against the
/// persisted SHA-256 hash, so raw numbers never leave configuration.
/// </summary>
public sealed class ConfiguredAdminPhoneNumberService : IConfiguredAdminPhoneNumberService
{
    private readonly PhoneNumberSet adminPhoneNumbers;
    private readonly PhoneNumberSet ownerPhoneNumbers;

    public ConfiguredAdminPhoneNumberService(IOptions<AdminPhoneNumberOptions> options)
    {
        adminPhoneNumbers = new PhoneNumberSet(options.Value.AdminPhoneNumbers);
        ownerPhoneNumbers = new PhoneNumberSet(options.Value.OwnerPhoneNumbers);
    }

    public bool IsConfiguredAdminPhoneNumber(string phoneNumber) => adminPhoneNumbers.Contains(phoneNumber);

    public bool IsConfiguredAdminPhoneNumberHash(string? phoneNumberHash) => adminPhoneNumbers.ContainsHash(phoneNumberHash);

    public bool IsConfiguredOwnerPhoneNumber(string phoneNumber) => ownerPhoneNumbers.Contains(phoneNumber);

    public bool IsConfiguredOwnerPhoneNumberHash(string? phoneNumberHash) => ownerPhoneNumbers.ContainsHash(phoneNumberHash);

    private sealed class PhoneNumberSet
    {
        private readonly HashSet<string> normalizedPhoneNumbers;
        private readonly HashSet<string> normalizedPhoneNumberHashes;

        public PhoneNumberSet(string configuredPhoneNumbers)
        {
            var configuredNumbers = SplitPhoneNumbers(configuredPhoneNumbers).ToArray();
            normalizedPhoneNumbers = configuredNumbers.ToHashSet(StringComparer.Ordinal);
            normalizedPhoneNumberHashes = configuredNumbers
                .Select(AuthenticationHashing.Sha256)
                .ToHashSet(StringComparer.Ordinal);
        }

        public bool Contains(string phoneNumber)
        {
            var normalized = NormalizePhoneNumber(phoneNumber);
            return normalized is not null && normalizedPhoneNumbers.Contains(normalized);
        }

        public bool ContainsHash(string? phoneNumberHash) =>
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
}
