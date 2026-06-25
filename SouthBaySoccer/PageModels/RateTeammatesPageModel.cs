using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Presents the STAT-8 rate-teammates flow for one completed match.
/// </summary>
public partial class RateTeammatesPageModel(
    IStatsClient statsClient,
    IRateTeammatesNavigator navigator,
    RateTeammatesOptions options) : ObservableObject
{
    public const string MatchIdQueryKey = "matchId";
    public const string RaterIdQueryKey = "raterId";
    public const string MatchSubtitleQueryKey = "subtitle";
    public const string IntroCopy =
        "Rate teammates 0-10, like a great game, and pick one MVP. You can't rate yourself.";
    public const string EmptyTitle = "No teammates to rate";
    public const string EmptyMessage = "This match has no rateable teammates yet.";
    public const string ErrorTitle = "Couldn't load ratings";
    public const string ErrorMessage = "Something went wrong loading this match. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to rate your teammates.";

    private Guid matchId = options.MatchId;
    private Guid raterId = options.RaterId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitRatingsCommand))]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _matchSubtitle = options.MatchSubtitle;

    [ObservableProperty]
    private ObservableCollection<RateTeammateRow> _teammates = [];

    [ObservableProperty]
    private RateTeammateRow? _selectedMvp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitRatingsCommand))]
    private bool _isBusy;

    /// <summary>True when the submit action can run.</summary>
    public bool CanSubmit => State == ViewState.Content && !IsBusy;

    /// <summary>Applies route parameters without referencing MAUI types.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(MatchIdQueryKey, out var matchValue)
            && Guid.TryParse(matchValue?.ToString(), out var parsedMatchId))
        {
            matchId = parsedMatchId;
        }

        if (query.TryGetValue(RaterIdQueryKey, out var raterValue)
            && Guid.TryParse(raterValue?.ToString(), out var parsedRaterId))
        {
            raterId = parsedRaterId;
        }

        if (query.TryGetValue(MatchSubtitleQueryKey, out var subtitle))
        {
            var subtitleText = subtitle?.ToString();
            if (!string.IsNullOrWhiteSpace(subtitleText))
            {
                MatchSubtitle = subtitleText;
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand]
    private void ToggleLike(RateTeammateRow? teammate)
    {
        if (teammate is not null)
        {
            teammate.Liked = !teammate.Liked;
        }
    }

    [RelayCommand]
    private void SelectMvp(RateTeammateRow? teammate)
    {
        if (teammate is null)
        {
            return;
        }

        SelectedMvp = ReferenceEquals(SelectedMvp, teammate) ? null : teammate;
        foreach (var row in Teammates)
        {
            row.IsMvp = ReferenceEquals(row, SelectedMvp);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSubmit))]
    private async Task SubmitRatings(CancellationToken cancellationToken)
    {
        if (!CanSubmit)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var ratings = Teammates
                .Where(row => row.PlayerId != raterId)
                .Select(row => new TeammateRatingDto(
                    row.PlayerId,
                    row.Rating,
                    row.Liked,
                    row.IsMvp))
                .ToArray();

            var result = await statsClient.SubmitRatingsAsync(
                matchId,
                raterId,
                ratings,
                cancellationToken);

            if (result.IsSuccess)
            {
                await navigator.GoBackAsync();
                return;
            }

            ApplyNonContentState(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyNonContentState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyNonContentState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var teammates = await statsClient.GetRateableTeammatesAsync(
                matchId,
                raterId,
                cancellationToken);

            ApplyTeammates(teammates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyNonContentState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyNonContentState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyTeammates(IReadOnlyList<RateableTeammateDto> teammates)
    {
        var rows = teammates
            .Where(item => item.Player.Id != raterId)
            .Select(RateTeammateRow.From)
            .ToArray();

        Teammates = new ObservableCollection<RateTeammateRow>(rows);
        SelectedMvp = rows.SingleOrDefault(row => row.IsMvp);

        if (Teammates.Count == 0)
        {
            ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
            return;
        }

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        Teammates = [];
        SelectedMvp = null;
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

/// <summary>Navigation boundary for the rate-teammates screen.</summary>
public interface IRateTeammatesNavigator
{
    /// <summary>Returns to the previous screen.</summary>
    Task GoBackAsync();
}

/// <summary>Default match context for the UI-first seed flow.</summary>
public sealed class RateTeammatesOptions
{
    public Guid MatchId { get; init; } = Guid.Parse("30000000-0000-0000-0000-000000000001");

    public Guid RaterId { get; init; } = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public string MatchSubtitle { get; init; } = "Sat \u00B7 Marina Field";
}

/// <summary>A bindable teammate row with STAT-8 rating, like, and MVP state.</summary>
public sealed partial class RateTeammateRow : ObservableObject
{
    private int rating;

    private RateTeammateRow(
        Guid playerId,
        string initials,
        string name,
        string detail,
        int rating,
        bool liked,
        bool isMvp)
    {
        PlayerId = playerId;
        Initials = initials;
        Name = name;
        Detail = detail;
        this.rating = CoerceRating(rating);
        Liked = liked;
        IsMvp = isMvp;
    }

    public Guid PlayerId { get; }

    public string Initials { get; }

    public string Name { get; }

    public string Detail { get; }

    public int Rating
    {
        get => rating;
        set
        {
            var coerced = CoerceRating(value);
            if (SetProperty(ref rating, coerced))
            {
                OnPropertyChanged(nameof(RatingValue));
                OnPropertyChanged(nameof(RatingText));
                OnPropertyChanged(nameof(RatingSemanticDescription));
            }
        }
    }

    public double RatingValue
    {
        get => Rating;
        set => Rating = (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public string RatingText => Rating.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string RatingSemanticDescription => $"Rating for {Name}: {RatingText} out of 10";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeSemanticDescription))]
    private bool _liked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MvpSemanticDescription))]
    private bool _isMvp;

    public string LikeSemanticDescription => Liked ? $"Unlike {Name}" : $"Like {Name}";

    public string MvpSemanticDescription => IsMvp ? $"{Name} is match MVP" : $"Select {Name} as match MVP";

    public static RateTeammateRow From(RateableTeammateDto teammate) =>
        new(
            teammate.Player.Id,
            teammate.Player.Initials,
            teammate.Player.DisplayName,
            teammate.Detail,
            teammate.Rating,
            teammate.IsLiked,
            teammate.IsMvp);

    private static int CoerceRating(int value) => Math.Clamp(value, 0, 10);
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
/// <summary>Shell-backed navigation for the rate-teammates screen.</summary>
public sealed class ShellRateTeammatesNavigator : IRateTeammatesNavigator
{
    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

public partial class RateTeammatesPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ApplyQueryAttributes(query);
        AppearingCommand.Execute(null);
    }
}
#endif


