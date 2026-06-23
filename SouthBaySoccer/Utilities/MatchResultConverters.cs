using System.Globalization;
using SouthBaySoccer.Controls;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Utilities;

/// <summary>
/// Converts <see cref="MatchResult"/> enum to display text for recent form badges.
/// </summary>
public class MatchResultConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MatchResult result)
        {
            return null;
        }

        return result switch
        {
            MatchResult.Win => "W",
            MatchResult.Draw => "D",
            MatchResult.Loss => "L",
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts <see cref="MatchResult"/> enum to <see cref="BadgeVariant"/> for recent form styling.
/// Win → Success, Draw → Warning, Loss → Danger
/// </summary>
public class MatchResultVariantConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MatchResult result)
        {
            return BadgeVariant.Neutral;
        }

        return result switch
        {
            MatchResult.Win => BadgeVariant.Success,
            MatchResult.Draw => BadgeVariant.Warning,
            MatchResult.Loss => BadgeVariant.Danger,
            _ => BadgeVariant.Neutral
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts <see cref="MatchResult"/> enum to semantic description for screen readers.
/// </summary>
public class MatchResultDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MatchResult result)
        {
            return null;
        }

        return result switch
        {
            MatchResult.Win => "Win",
            MatchResult.Draw => "Draw",
            MatchResult.Loss => "Loss",
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
