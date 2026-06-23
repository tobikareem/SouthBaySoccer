using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Page model for the Player Profile screen (PROF-5). Loads the current player's profile
/// through <see cref="IProfileClient"/>, maps the result onto loading / content / empty / error /
/// offline view states for <c>StateView</c>, and exposes navigation and external-launch commands.
/// </summary>
public partial class ProfilePageModel(
    IProfileClient profileClient,
    IExternalLauncher externalLauncher,
    PickupPalOptions pickupPalOptions) : ObservableObject
{
    public const string EmptyTitle = "Profile not found";
    public const string EmptyMessage = "Your profile data is not available.";
    public const string ErrorTitle = "Couldn't load your profile";
    public const string ErrorMessage = "Something went wrong loading your profile. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your profile.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private PlayerProfileDto? _profile;

    [ObservableProperty]
    private string _pendingNote = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadProfileAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadProfileAsync(cancellationToken);

    [RelayCommand]
    private async Task EditOnPickupPal()
    {
        try
        {
            await externalLauncher.OpenUrlAsync(pickupPalOptions.AccountEditUri);
        }
        catch (Exception)
        {
            // Suppress errors from external launcher; user will see if nothing happens
        }
    }

    [RelayCommand]
    private async Task OpenLeaderboard()
    {
        await Shell.Current.GoToAsync("leaderboard");
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var profile = await profileClient.GetProfileAsync(
                SeedFixtures.CurrentPlayerId,
                cancellationToken);

            if (profile is null)
            {
                ApplyErrorState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            ApplyProfile(profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyErrorState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyErrorState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyProfile(PlayerProfileDto profile)
    {
        Profile = profile;
        PendingNote = profile.PendingConfirmationNote ?? string.Empty;

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyErrorState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
