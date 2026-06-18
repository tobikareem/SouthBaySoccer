namespace SouthBaySoccer.Application.Abstractions.Messaging;

/// <summary>
/// Application port for transactional email delivery (SendGrid in Infrastructure).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a transactional email message.
    /// </summary>
    /// <param name="to">The recipient email address.</param>
    /// <param name="subject">The message subject line.</param>
    /// <param name="body">The message body.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes when the message has been accepted for delivery.</returns>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
