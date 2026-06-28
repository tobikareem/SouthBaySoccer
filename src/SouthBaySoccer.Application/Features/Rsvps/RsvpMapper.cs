using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

internal static class RsvpMapper
{
    public static RsvpResultModel ToModel(RsvpMutationResult result) =>
        new(
            result.SessionId,
            result.PlayerProfileId,
            result.State.ToString(),
            result.RsvpResponseId,
            result.WaitlistEntryId,
            result.WaitlistPosition,
            result.PromotedPlayerProfileId);
}
