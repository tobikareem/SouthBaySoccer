using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Controls;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads and presents the signed-in player's profile.
/// </summary>
public partial class ProfilePageModel(
    IProfileClient profileClient,
    IProfileExternalLauncher externalLauncher,
    IProfileNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "Profile not found";
    public const string EmptyMessage = "Your profile data is not available.";
    public const string ErrorTitle = "Couldn't load your profile";
    public const string ErrorMessage = "Something went wrong loading your profile. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your profile.";
    public const string ExternalLaunchError = "Pickup Pal could not be opened. Please try again.";

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

    partial void OnActionMessageChanged(string value) => OnPropertyChanged(nameof(HasActionMessage));

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

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        ClearProfile();
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var profile = await profileClient.GetCurrentProfileAsync(cancellationToken);

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
