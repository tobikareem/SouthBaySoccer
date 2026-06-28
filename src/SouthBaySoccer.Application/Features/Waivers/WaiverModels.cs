namespace SouthBaySoccer.Application.Features.Waivers;

public sealed record WaiverDocumentModel(
    Guid WaiverDocumentId,
    string Version,
    string Title,
    string ContentHash,
    DateTime? PublishedAtUtc);

public sealed record WaiverAcceptanceModel(
    Guid WaiverAcceptanceId,
    Guid WaiverDocumentId,
    string Version,
    DateTime AcceptedAtUtc);

public sealed record AcceptCurrentWaiverCommand;

public sealed record WaiverEligibilityModel(
    bool HasCurrentWaiver,
    Guid? CurrentWaiverDocumentId,
    string? CurrentVersion,
    string? Reason);
