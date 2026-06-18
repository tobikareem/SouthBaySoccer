namespace SouthBaySoccer.Application.Abstractions.Messaging;

/// <summary>
/// Application port for transactional SMS delivery (Twilio in Infrastructure).
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends a transactional SMS message.
    /// </summary>
    /// <param name="toPhoneNumber">The recipient phone number in E.164 format.</param>
    /// <param name="message">The message text.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes when the message has been accepted for delivery.</returns>
    Task SendAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default);
}
