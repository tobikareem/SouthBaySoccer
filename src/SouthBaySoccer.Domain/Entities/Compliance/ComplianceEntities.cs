using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Compliance;

/// <summary>Represents a versioned waiver and code-of-conduct document.</summary>
public class WaiverDocument : BaseEntity
{
    /// <summary>Gets or sets the waiver version label.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Gets or sets the document title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Gets or sets the content hash.</summary>
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>Gets or sets the document status.</summary>
    public WaiverDocumentStatus Status { get; set; }
    /// <summary>Gets or sets when this version was published.</summary>
    public DateTime? PublishedAtUtc { get; set; }
}

/// <summary>Represents a player acceptance of a waiver version.</summary>
public class WaiverAcceptance : BaseEntity
{
    /// <summary>Gets or sets the player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the waiver document id.</summary>
    public Guid WaiverDocumentId { get; set; }
    /// <summary>Gets or sets when acceptance occurred.</summary>
    public DateTime AcceptedAtUtc { get; set; }
    /// <summary>Gets or sets the accepted document content hash.</summary>
    public string ContentHash { get; set; } = string.Empty;
}
