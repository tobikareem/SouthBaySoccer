namespace SouthBaySoccer.Contracts.Authentication;

/// <summary>Result of starting phone sign-in. Tokens are present only when no verification is required.</summary>
public sealed record PhoneSignInStartResponse(
    bool VerificationRequired,
    string? PhoneMasked,
    string? DisplayName,
    AuthenticationTokensResponse? Tokens);

/// <summary>Completes a verified sign-in with the login token the Pickup Pal bot linked back with.</summary>
public sealed record CompleteWhatsAppLoginRequest(string Token, bool RememberDevice);

/// <summary>Validates a registration token before showing the details form.</summary>
public sealed record ValidateRegistrationTokenRequest(string Token);

/// <summary>The phone the token was minted for, masked for display.</summary>
public sealed record RegistrationTokenValidationResponse(string PhoneMasked);

/// <summary>Pre-flight email uniqueness check so a duplicate never spends the registration token.</summary>
public sealed record CheckEmailAvailabilityRequest(string Email);

public sealed record CheckEmailAvailabilityResponse(bool IsAvailable);

/// <summary>Creates the Pickup Pal account with a WhatsApp-bound registration token.</summary>
public sealed record RegisterWithWhatsAppRequest(
    string Token,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? PreferredPosition,
    string TermsVersion,
    DateTime TermsAcceptedAtUtc);

/// <summary>What the welcome screen shows after a linked account is created.</summary>
public sealed record RegistrationCompletedResponse(
    AuthenticationTokensResponse Tokens,
    string FirstName,
    string PhoneMasked,
    IReadOnlyList<string> GroupNames,
    bool HistorySyncPending);

public sealed record TermsVersionResponse(string Version);
