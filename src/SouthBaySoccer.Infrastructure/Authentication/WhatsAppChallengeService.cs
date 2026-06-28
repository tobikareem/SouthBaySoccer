using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// EF-backed one-time WhatsApp challenge store.
/// </summary>
public sealed class WhatsAppChallengeService(
    SouthBaySoccerDbContext dbContext,
    IClock clock,
    IWhatsAppChallengeTokenGenerator tokenGenerator,
    IWhatsAppChallengeDeliverySender deliverySender,
    IOptions<WhatsAppChallengeOptions> options) : IWhatsAppChallengeService
{
    public async Task<WhatsAppChallengeIssueResult> CreateChallengeAsync(
        WhatsAppChallengeIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var challengeToken = tokenGenerator.CreateToken();
        var challenge = new WhatsAppSignInChallenge
        {
            Id = Guid.NewGuid(),
            ChallengeId = Guid.NewGuid().ToString("N"),
            ChallengeTokenHash = AuthenticationHashing.Sha256(challengeToken),
            PhoneNumberHash = AuthenticationHashing.Sha256(request.PhoneNumber),
            MaskedPhoneNumber = request.MaskedPhoneNumber,
            CallbackUriHash = AuthenticationHashing.Sha256(request.CallbackUri),
            ExpiresAtUtc = now.Add(options.Value.ChallengeLifetime),
            CreatedAt = now,
            CreatedBy = "system",
        };

        dbContext.WhatsAppSignInChallenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken);

        await deliverySender.SendAsync(
            new WhatsAppChallengeDeliveryRequest(
                request.PhoneNumber,
                challenge.MaskedPhoneNumber ?? request.MaskedPhoneNumber,
                challengeToken,
                request.CallbackUri,
                challenge.ExpiresAtUtc),
            cancellationToken);

        return new WhatsAppChallengeIssueResult(
            challenge.ChallengeId,
            challenge.MaskedPhoneNumber ?? request.MaskedPhoneNumber,
            challenge.ExpiresAtUtc);
    }

    public async Task<WhatsAppChallengeVerificationResult> VerifyChallengeAsync(
        WhatsAppChallengeVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = AuthenticationHashing.Sha256(request.ChallengeToken);
        var callbackUriHash = AuthenticationHashing.Sha256(request.CallbackUri);
        var now = clock.UtcNow;

        var challenge = await dbContext.WhatsAppSignInChallenges
            .SingleOrDefaultAsync(x => x.ChallengeTokenHash == tokenHash, cancellationToken);

        if (challenge is null ||
            challenge.ConsumedAtUtc is not null ||
            challenge.ExpiresAtUtc <= now ||
            !string.Equals(challenge.CallbackUriHash, callbackUriHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("WhatsApp challenge is invalid or expired.");
        }

        var phoneNumberHash = challenge.PhoneNumberHash
            ?? throw new InvalidOperationException("WhatsApp challenge is invalid or expired.");

        challenge.ConsumedAtUtc = now;
        challenge.UpdatedAt = now;
        challenge.UpdatedBy = "system";
        await dbContext.SaveChangesAsync(cancellationToken);

        return new WhatsAppChallengeVerificationResult(
            phoneNumberHash,
            challenge.MaskedPhoneNumber ?? "***");
    }
}