using System.Text.RegularExpressions;

namespace SouthBaySoccer.Functions.Pipeline;

public sealed partial class CorrelationIdProvider : ICorrelationIdProvider
{
    public string Resolve(IEnumerable<string>? candidateValues)
    {
        var candidate = candidateValues?
            .Select(value => value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return IsValid(candidate) ? candidate! : Guid.NewGuid().ToString("N");
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && SafeCorrelationId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCorrelationId();
}

