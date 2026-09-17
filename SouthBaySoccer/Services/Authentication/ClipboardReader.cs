namespace SouthBaySoccer.Services.Authentication;

public sealed class ClipboardReader : IClipboardReader
{
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null;
    }
}
