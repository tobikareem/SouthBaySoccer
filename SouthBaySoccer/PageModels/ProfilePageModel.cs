using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Controls;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads and presents the signed-in player's profile.
/// </summary>
public partial class ProfilePageModel(
    IProfileClient profileClient,
    IProfileExternalLauncher externalLauncher,
    IProfileNavigator navigator,
    IAuthenticationCoordinator authenticationCoordinator,
    IUserDialogService dialogService) : ObservableObject
{
    public const string EmptyTitle = "Profile not found";
    public const string EmptyMessage = "Your profile data is not available.";
    public const string ErrorTitle = "Couldn't load your profile";
    public const string ErrorMessage = "Something went wrong loading your profile. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your profile.";
    public const string ExternalLaunchError = "Pickup Pal could not be opened. Please try again.";
    public const string PlayerIdQueryKey = "playerId";
    public const string SignOutConfirmTitle = "Sign out?";
    public const string SignOutConfirmMessage =
        "You'll be signed out on this device. Sign in again with any phone number connected to a Pickup Pal account.";

    private Guid? requestedPlayerId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private PlayerProfileDto? _profile;

    [ObservableProperty]
    private IReadOnlyList<ProfileFormBadge> _recentForm = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingNote))]
    private string _pendingNote = string.Empty;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasPendingNote => !string.IsNullOrWhiteSpace(PendingNote);

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public bool CanEditProfile => requestedPlayerId is null;

    public string MatchesText => Profile?.CareerStats.Matches.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string GoalsText => Profile?.CareerStats.Goals.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string AssistsText => Profile?.CareerStats.Assists.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string AverageRatingText =>
        Profile?.CareerStats.AverageRating.ToString("0.0", CultureInfo.InvariantCulture) ?? "0.0";

    public string MvpAwardsText => Profile?.CareerStats.MvpAwards.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string LikesText => Profile?.CareerStats.Likes.ToString(CultureInfo.InvariantCulture) ?? "0";

    partial void OnProfileChanged(PlayerProfileDto? value)
    {
        OnPropertyChanged(nameof(MatchesText));
        OnPropertyChanged(nameof(GoalsText));
        OnPropertyChanged(nameof(AssistsText));
        OnPropertyChanged(nameof(AverageRatingText));
        OnPropertyChanged(nameof(MvpAwardsText));
        OnPropertyChanged(nameof(LikesText));
    }

    partial void OnActionMessageChanged(string value) => OnPropertyChanged(nameof(HasActionMessage));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        requestedPlayerId = null;

        if (query.TryGetValue(PlayerIdQueryKey, out var value)
            && Guid.TryParse(value?.ToString(), out var parsedPlayerId)
            && parsedPlayerId != Guid.Empty)
        {
            requestedPlayerId = parsedPlayerId;
        }

        OnPropertyChanged(nameof(CanEditProfile));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadProfileAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadProfileAsync(cancellationToken);

    [RelayCommand]
    private async Task EditOnPickupPal(CancellationToken cancellationToken)
    {
        ActionMessage = string.Empty;

        try
        {
            if (!await externalLauncher.OpenAccountAsync(cancellationToken))
            {
                ActionMessage = ExternalLaunchError;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ActionMessage = ExternalLaunchError;
        }
    }

    [RelayCommand]
    private Task OpenLeaderboard() => navigator.OpenLeaderboardAsync();

    // Sign out / switch account. Only offered on the signed-in player's own profile (CanEditProfile).
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SignOut(CancellationToken cancellationToken)
    {
        var confirmed = await dialogService.ShowConfirmationAsync(
            SignOutConfirmTitle,
            SignOutConfirmMessage,
            "Sign out",
            "Cancel",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await authenticationCoordinator.SignOutAsync(cancellationToken);
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        ClearProfile();
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var profile = requestedPlayerId is Guid playerId
                ? await profileClient.GetProfileAsync(playerId, cancellationToken)
                : await profileClient.GetCurrentProfileAsync(cancellationToken);

            if (profile is null)
            {
                ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            Profile = profile;
            RecentForm = profile.RecentForm.Select(ProfileFormBadge.FromResult).ToArray();
            PendingNote = profile.PendingConfirmationNote ?? string.Empty;
            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
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

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        ClearProfile();
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    private void ClearProfile()
    {
        Profile = null;
        RecentForm = [];
        PendingNote = string.Empty;
    }
}

public sealed record ProfileFormBadge(string Text, BadgeVariant Variant, string Description)
{
    public static ProfileFormBadge FromResult(MatchResult result) =>
        result switch
        {
            MatchResult.Win => new("W", BadgeVariant.Success, "Win"),
            MatchResult.Draw => new("D", BadgeVariant.Warning, "Draw"),
            MatchResult.Loss => new("L", BadgeVariant.Danger, "Loss"),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported match result.")
        };
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class ProfilePageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ApplyQueryAttributes(query);
    }
}
#endif
