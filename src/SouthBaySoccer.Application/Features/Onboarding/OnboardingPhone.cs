using SouthBaySoccer.Application.Features.Players;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Phone helpers shared by the onboarding handlers. Every stored value is the hash or mask of the
/// E.164 form (<c>+digits</c>), which is the same shape the Pickup Pal sync service persists on
/// <c>PlayerProfile</c>, so registrations, pending sign-ins, and profiles agree on one hash.
/// </summary>
internal static class OnboardingPhone
{
    /// <summary>Formats digits-only phone input as E.164.</summary>
    public static string ToE164(string phoneNumberDigits) =>
        $"+{new string(phoneNumberDigits.Where(char.IsDigit).ToArray())}";

    /// <summary>Hashes the E.164 form of the phone number.</summary>
    public static string Hash(string phoneNumberDigits) => PhonePrivacy.Hash(ToE164(phoneNumberDigits));

    /// <summary>Masks the phone number for display, keeping only the last four digits.</summary>
    public static string Mask(string phoneNumberDigits) => PhonePrivacy.Mask(ToE164(phoneNumberDigits));
}
