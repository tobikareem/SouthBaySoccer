using System;
using System.Linq;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Filters group lists using configured, case-insensitive name fragments.</summary>
public sealed class GroupNameVisibility
{
    public const string DefaultExcludedNamePatterns = "test,tmp,120363";

    private readonly string[] _excludedNamePatterns;

    public GroupNameVisibility(string excludedNamePatterns = DefaultExcludedNamePatterns)
    {
        ArgumentNullException.ThrowIfNull(excludedNamePatterns);
        _excludedNamePatterns = excludedNamePatterns
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsVisible(string? name) =>
        name is null || !_excludedNamePatterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
}
