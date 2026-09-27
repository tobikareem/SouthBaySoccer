namespace SouthBaySoccer.Services.Authentication;

/// <summary>Reads clipboard text so the "paste the link" fallback is testable without a device.</summary>
public interface IClipboardReader
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken);
}
