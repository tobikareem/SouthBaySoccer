using System;

namespace SouthBaySoccer.Domain.Entities.Common;

/// <summary>
/// Abstract base type for all domain entities. Provides the identity, audit, and
/// soft-delete fields every persisted entity in the system shares.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity. Defaults to a new
    /// <see cref="Guid"/>; primary keys are always GUIDs, never auto-increment integers.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the UTC timestamp recorded when this entity was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the actor that created this entity, when known.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp recorded when this entity was last updated,
    /// or <see langword="null"/> if it has never been updated since creation.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the actor that last updated this entity, when known.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this entity has been soft-deleted.
    /// Records are never physically removed; this flag is set to <see langword="true"/> instead.
    /// </summary>
    public bool IsDeleted { get; set; }
}
