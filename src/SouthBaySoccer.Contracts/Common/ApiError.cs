namespace SouthBaySoccer.Contracts.Common;

/// <summary>
/// A transport-friendly error returned by the API. <paramref name="Code"/> is a stable,
/// machine-readable identifier; <paramref name="Message"/> is a human-readable description
/// safe to surface to the client.
/// </summary>
/// <param name="Code">A stable, machine-readable error code.</param>
/// <param name="Message">A human-readable, client-safe error description.</param>
public sealed record ApiError(string Code, string Message);
