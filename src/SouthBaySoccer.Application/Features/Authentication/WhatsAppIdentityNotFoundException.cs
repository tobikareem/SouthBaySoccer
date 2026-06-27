namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Thrown when a verified WhatsApp phone number cannot be mapped to an application identity.
/// </summary>
public sealed class WhatsAppIdentityNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WhatsAppIdentityNotFoundException"/> class.
    /// </summary>
    /// <param name="maskedPhoneNumber">The masked phone number display value.</param>
    public WhatsAppIdentityNotFoundException(string maskedPhoneNumber)
        : base($"No player identity is linked to phone {maskedPhoneNumber}.")
    {
        MaskedPhoneNumber = maskedPhoneNumber;
    }

    /// <summary>
    /// Gets the masked phone number display value.
    /// </summary>
    public string MaskedPhoneNumber { get; }
}

