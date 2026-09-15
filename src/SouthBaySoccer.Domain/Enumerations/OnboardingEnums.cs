namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>Identifies how a player registration was proven and created.</summary>
public enum RegistrationSource
{
    /// <summary>The player proved phone ownership with a Pickup Pal <c>!!register</c> WhatsApp token.</summary>
    WhatsAppToken,
}

/// <summary>Tracks the local-first lifecycle of a player registration against Pickup Pal.</summary>
public enum PlayerRegistrationStatus
{
    /// <summary>The registration is stored locally and the Pickup Pal account has not been created yet.</summary>
    PendingExternal,

    /// <summary>Pickup Pal created the account and local identity records were synced.</summary>
    Completed,

    /// <summary>The Pickup Pal call failed; the local record is kept for retry or reconciliation.</summary>
    ExternalFailed,
}
