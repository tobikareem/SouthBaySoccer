namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Signs in a player by confirming the phone number exists in Pickup Pal.
/// </summary>
/// <param name="PhoneNumber">The phone number submitted by the client.</param>
public sealed record SignInByPhoneCommand(string PhoneNumber);
