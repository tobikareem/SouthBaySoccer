namespace SouthBaySoccer.Contracts.Compliance;

public sealed record WaiverDocumentResponse(
    Guid WaiverDocumentId,
    string Version,
    string Title,
    string ContentHash,
    DateTime? PublishedAtUtc);

public sealed record WaiverAcceptanceResponse(
    Guid WaiverAcceptanceId,
    Guid WaiverDocumentId,
    string Version,
    DateTime AcceptedAtUtc);

public sealed record WaiverEligibilityResponse(
    bool HasCurrentWaiver,
    Guid? CurrentWaiverDocumentId,
    string? CurrentVersion,
    string? Reason);
