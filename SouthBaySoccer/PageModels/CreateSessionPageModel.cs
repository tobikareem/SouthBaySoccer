using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Page model for the admin "Create session" screen (ADMIN-4). Loads form defaults and the caller's
/// <c>CanManageSessions</c> permission through <see cref="ISessionAdminClient"/>, builds a live
/// player-facing preview from the entered details, validates required fields / capacity / check-in
/// window, and publishes the session to the team Sessions feed. A successful publish navigates to the
/// new session via <see cref="ISessionsNavigator"/>; repeated taps reuse the published session rather
/// than creating duplicates.
/// </summary>
/// <remarks>
/// This file is intentionally free of MAUI types so the behaviour is unit-testable in the plain client
/// test project. Shell back-navigation lives in <c>CreateSessionPageModel.Navigation.cs</c>, compiled
/// only into the MAUI app.
/// </remarks>
public partial class CreateSessionPageModel(
    ISessionAdminClient adminClient,
    ISessionsNavigator navigator) : ObservableObject
{
    public const string DeniedTitle = "Admin access required";
    public const string DeniedMessage = "You need session-management permission to create a session.";
    public const string ErrorTitle = "Couldn't open create session";
    public const string ErrorMessage = "Something went wrong loading the form. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to create and publish a session.";
    public const string OfflinePublishMessage = "You're offline. Reconnect to publish this session.";
    public const string GenericPublishError = "Something went wrong publishing the session. Please try again.";
    public const string PreviewStatusLabel = "Ready for RSVP";

    private int _checkInLeadMinutes;
    private int _checkInCloseOffsetMinutes;
    private Guid? _draftId;
    private bool _isPublished;
    private Guid _publishedSessionId;
    private bool _isApplyingSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewDateLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private DateTime _gameDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTimeLabel))]
    [NotifyPropertyChangedFor(nameof(CheckInPreviewLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private TimeSpan _startTime = new(19, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private DateTime _rsvpCloseDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private TimeSpan? _rsvpDeadline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private TimeSpan _rsvpCloseTime = new(18, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckInPreviewLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private TimeSpan _checkInOpen = new(18, 50, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckInPreviewLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private TimeSpan _checkInClose = new(19, 5, 0);

    [ObservableProperty]
    private string _venueQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVenueResults))]
    private IReadOnlyList<VenueDto> _venueResults = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVenue))]
    [NotifyPropertyChangedFor(nameof(PreviewTitle))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private VenueDto? _selectedVenue;

    [ObservableProperty]
    private string _savedVenueHint = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _formats = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFormatLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private int _selectedFormatIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private int _minimumCapacity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private int _maximumCapacity = 40;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFormatLabel))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private int _capacity = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private IReadOnlyList<string> _teamOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private int _selectedTeamIndex;

    [ObservableProperty]
    private string _feedLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManagedSessions))]
    private IReadOnlyList<ManagedSessionDto> _managedSessions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingSession))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    private Guid? _editingSessionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    private bool _isPublishing;

    /// <summary>True when at least one venue search result is available to choose from.</summary>
    public bool HasVenueResults => VenueResults.Count > 0;

    /// <summary>True once a venue has been selected for the session.</summary>
    public bool HasSelectedVenue => SelectedVenue is not null;

    /// <summary>True when a validation message should be surfaced under the form.</summary>
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>True when there are created sessions the admin can open for updates.</summary>
    public bool HasManagedSessions => ManagedSessions.Count > 0;

    /// <summary>True when the form is editing an existing published session instead of creating a new one.</summary>
    public bool IsEditingSession => EditingSessionId is not null;

    /// <summary>Primary form action text for create vs. update mode.</summary>
    public string PrimaryActionText => IsEditingSession ? "Update session" : "Publish to team";

    /// <summary>Currently selected game format (e.g. "7v7"), or empty when none is selected.</summary>
    public string SelectedFormat =>
        SelectedFormatIndex >= 0 && SelectedFormatIndex < Formats.Count
            ? Formats[SelectedFormatIndex]
            : string.Empty;

    /// <summary>Number of teams to set up; index 0 maps to 2 teams, 1 to 3, and so on.</summary>
    public int SelectedTeamCount =>
        IsValidTeamSelection
            ? SelectedTeamIndex + 2
            : 0;

    /// <summary>Check-in window preview, e.g. "Check-in 7:30 PM - closes 7:45 PM".</summary>
    public string CheckInPreviewLabel =>
        $"Check-in {FormatTime(CheckInOpen)} - closes {FormatTime(CheckInClose)}";

    /// <summary>Player-preview card title, e.g. "Marina Field - Saturday pickup".</summary>
    public string PreviewTitle =>
        SelectedVenue is null ? "New session" : $"{SelectedVenue.Name} - Saturday pickup";

    /// <summary>Player-preview date chip, e.g. "Jun 27".</summary>
    public string PreviewDateLabel => FormatDate(GameDate);

    /// <summary>Player-preview time chip, e.g. "7:40 PM".</summary>
    public string PreviewTimeLabel => FormatTime(StartTime);

    /// <summary>Player-preview format/capacity chip, e.g. "7v7 - 20 spots".</summary>
    public string PreviewFormatLabel => $"{SelectedFormat} - {Capacity} spots";

    /// <summary>True when the form is complete and a publish may be attempted.</summary>
    public bool CanPublish =>
        CanPublishToTeam()
        && RsvpDeadlineLocal <= SessionStartLocal;

    private bool CanPublishToTeam() =>
        State == ViewState.Content
        && HasSelectedVenue
        && !string.IsNullOrWhiteSpace(SelectedFormat)
        && Capacity >= MinimumCapacity
        && Capacity <= MaximumCapacity
        && CheckInOpen < CheckInClose
        && RsvpCloseDate != default
        && RsvpDeadline is not null
        && RsvpCloseTime != default
        && IsValidTeamSelection
        && !IsPublishing;

    private bool IsValidTeamSelection =>
        SelectedTeamIndex >= 0
        && SelectedTeamIndex < TeamOptions.Count
        && SelectedTeamIndex <= 2;

    private DateTime SessionStartLocal => GameDate.Date + StartTime;

    private DateTime RsvpDeadlineLocal => RsvpCloseDate.Date + RsvpCloseTime;

    /// <summary>Loads the form defaults and the caller's session-management permission.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Load(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;

        try
        {
            var defaults = await adminClient.GetDefaultsAsync(cancellationToken);

            if (!defaults.CanManageSessions)
            {
                ApplyState(ViewState.Empty, DeniedTitle, DeniedMessage);
                return;
            }

            ApplyDefaults(defaults);
            await RefreshManagedSessionsAsync(cancellationToken);

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
            ApplyState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    /// <summary>Searches venues for the current <see cref="VenueQuery"/> and refreshes the results list.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SearchVenues(CancellationToken cancellationToken)
    {
        try
        {
            VenueResults = await adminClient.SearchVenuesAsync(VenueQuery, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Venue search is a convenience; a transient failure should not break the form.
            VenueResults = [];
        }
    }

    /// <summary>Selects a venue from the search results and collapses the result list.</summary>
    [RelayCommand]
    private void SelectVenue(VenueDto? venue)
    {
        if (venue is null)
        {
            return;
        }

        SelectedVenue = venue;
        VenueQuery = venue.Name;
        SavedVenueHint = venue.Locality;
        VenueResults = [];
        ValidationMessage = string.Empty;
    }

    /// <summary>Loads an existing session into the form so the admin can update it.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task EditManagedSession(ManagedSessionDto? session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            var editable = await adminClient.GetSessionForEditAsync(session.SessionId, cancellationToken);
            if (editable is null)
            {
                ValidationMessage = "The selected session could not be opened for updates.";
                return;
            }

            ApplySessionForEdit(editable);
            ValidationMessage = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ValidationMessage = OfflinePublishMessage;
        }
        catch (Exception)
        {
            ValidationMessage = "Something went wrong opening the session for updates.";
        }
    }

    /// <summary>
    /// Validates the form, creates a draft, and publishes it to the team feed. Idempotent: once
    /// published, re-invoking navigates to the existing session instead of creating a duplicate.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanPublishToTeam))]
    private async Task PublishToTeam(CancellationToken cancellationToken)
    {
        if (EditingSessionId is not null)
        {
            await UpdateExistingSessionAsync(cancellationToken);
            return;
        }

        if (_isPublished)
        {
            await navigator.GoToSessionAsync(_publishedSessionId);
            return;
        }

        var validationError = Validate();
        if (validationError is not null)
        {
            ValidationMessage = validationError;
            return;
        }

        ValidationMessage = string.Empty;
        IsPublishing = true;

        try
        {
            // Reuse a previously created draft on retry so a failed publish never orphans a draft
            // or creates duplicates when the create succeeded but the publish did not.
            if (_draftId is null)
            {
                var draft = await adminClient.CreateDraftAsync(BuildCommand(), cancellationToken);
                if (!draft.IsSuccess)
                {
                    ValidationMessage = draft.ErrorMessage ?? GenericPublishError;
                    return;
                }

                _draftId = draft.SessionId;
            }

            var published = await adminClient.PublishAsync(_draftId.Value, cancellationToken);
            if (!published.IsSuccess)
            {
                ValidationMessage = published.ErrorMessage ?? GenericPublishError;
                return;
            }

            _isPublished = true;
            _publishedSessionId = published.SessionId;
            await RefreshManagedSessionsAsync(cancellationToken);
            await navigator.GoToSessionAsync(published.SessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ValidationMessage = OfflinePublishMessage;
        }
        catch (Exception)
        {
            ValidationMessage = GenericPublishError;
        }
        finally
        {
            IsPublishing = false;
        }
    }

    partial void OnVenueQueryChanged(string value)
    {
        // Editing the query after a selection invalidates it until the admin re-picks a venue.
        if (SelectedVenue is not null && !string.Equals(value, SelectedVenue.Name, StringComparison.Ordinal))
        {
            SelectedVenue = null;
        }
    }

    partial void OnStartTimeChanged(TimeSpan value)
    {
        if (_isApplyingSession)
        {
            return;
        }

        CheckInOpen = value - TimeSpan.FromMinutes(_checkInLeadMinutes);
        CheckInClose = value + TimeSpan.FromMinutes(_checkInCloseOffsetMinutes);
    }

    partial void OnGameDateChanged(DateTime value)
    {
        RsvpCloseDate = value.Date;
    }

    partial void OnRsvpDeadlineChanged(TimeSpan? value)
    {
        if (value is not null)
        {
            RsvpCloseTime = value.Value;
        }
    }

    partial void OnRsvpCloseTimeChanged(TimeSpan value)
    {
        RsvpDeadline = value;
    }

    private async Task UpdateExistingSessionAsync(CancellationToken cancellationToken)
    {
        var validationError = Validate();
        if (validationError is not null)
        {
            ValidationMessage = validationError;
            return;
        }

        ValidationMessage = string.Empty;
        IsPublishing = true;

        try
        {
            var updated = await adminClient.UpdateSessionAsync(
                EditingSessionId!.Value,
                BuildCommand(),
                cancellationToken);

            if (!updated.IsSuccess)
            {
                ValidationMessage = updated.ErrorMessage ?? GenericPublishError;
                return;
            }

            await RefreshManagedSessionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ValidationMessage = OfflinePublishMessage;
        }
        catch (Exception)
        {
            ValidationMessage = GenericPublishError;
        }
        finally
        {
            IsPublishing = false;
        }
    }

    private async Task RefreshManagedSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            ManagedSessions = await adminClient.ListManagedSessionsAsync(cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ManagedSessions = [];
        }
    }

    private void ApplySessionForEdit(ManagedSessionEditDto session)
    {
        _isApplyingSession = true;
        try
        {
            EditingSessionId = session.SessionId;
            _isPublished = true;
            _publishedSessionId = session.SessionId;
            _draftId = null;
            ApplyCommand(session.Command);
        }
        finally
        {
            _isApplyingSession = false;
        }
    }

    private void ApplyCommand(CreateSessionCommand command)
    {
        GameDate = command.GameDateLocal.Date;
        StartTime = command.StartTimeLocal;
        RsvpCloseDate = command.GameDateLocal.Date;
        RsvpDeadline = command.RsvpDeadlineLocal;
        RsvpCloseTime = command.RsvpDeadlineLocal ?? command.StartTimeLocal - TimeSpan.FromHours(1);
        CheckInOpen = command.CheckInOpenLocal;
        CheckInClose = command.CheckInCloseLocal;
        Capacity = command.Capacity;
        SelectedFormatIndex = Math.Max(0, Formats.ToList().IndexOf(command.Format));
        SelectedTeamIndex = command.TeamCount - 2;
        SelectVenue(new VenueDto(
            command.VenueId ?? Guid.Empty,
            command.VenueName,
            "Managed session",
            false));
    }

    private void ApplyDefaults(CreateSessionDefaultsDto defaults)
    {
        _checkInLeadMinutes = defaults.CheckInLeadMinutes;
        _checkInCloseOffsetMinutes = defaults.CheckInCloseOffsetMinutes;
        GameDate = defaults.DefaultGameDateLocal;
        StartTime = defaults.DefaultStartTimeLocal;
        RsvpCloseDate = defaults.DefaultGameDateLocal.Date;
        RsvpDeadline = defaults.DefaultRsvpDeadlineLocal ?? defaults.DefaultStartTimeLocal - TimeSpan.FromHours(1);
        CheckInOpen = defaults.DefaultStartTimeLocal - TimeSpan.FromMinutes(defaults.CheckInLeadMinutes);
        CheckInClose = defaults.DefaultStartTimeLocal + TimeSpan.FromMinutes(defaults.CheckInCloseOffsetMinutes);
        Formats = defaults.Formats;
        SelectedFormatIndex = defaults.DefaultFormatIndex;
        MinimumCapacity = defaults.MinimumCapacity;
        MaximumCapacity = defaults.MaximumCapacity;
        Capacity = defaults.DefaultCapacity;
        TeamOptions = defaults.TeamOptions;
        SelectedTeamIndex = defaults.DefaultTeamIndex;
        FeedLabel = defaults.FeedLabel;
        SelectVenue(defaults.SavedVenue);

        // SelectVenue recomputes derived preview labels; force a check-in refresh after lead/offset land.
        OnPropertyChanged(nameof(CheckInPreviewLabel));
    }

    private string? Validate()
    {
        if (GameDate == default)
        {
            return "Choose a game date.";
        }

        if (StartTime < TimeSpan.Zero || StartTime >= TimeSpan.FromDays(1))
        {
            return "Choose a start time.";
        }

        if (!HasSelectedVenue || string.IsNullOrWhiteSpace(VenueQuery))
        {
            return "Choose a venue to continue.";
        }

        if (string.IsNullOrWhiteSpace(SelectedFormat))
        {
            return "Select a game format.";
        }

        if (Capacity < MinimumCapacity)
        {
            return $"Capacity must be at least {MinimumCapacity}.";
        }

        if (Capacity < 1)
        {
            return "Capacity must be positive.";
        }

        if (Capacity > MaximumCapacity)
        {
            return $"Capacity cannot exceed {MaximumCapacity}.";
        }

        if (!IsValidTeamSelection)
        {
            return "Select a team setup.";
        }

        if (CheckInOpen >= CheckInClose)
        {
            return "Check-in must open before it closes.";
        }

        if (RsvpCloseDate == default || RsvpDeadline is null || RsvpCloseTime == default)
        {
            return "Set an RSVP deadline.";
        }

        if (RsvpDeadlineLocal > SessionStartLocal)
        {
            return "RSVP close cannot be after session start.";
        }

        return null;
    }

    private CreateSessionCommand BuildCommand() =>
        new(
            GameDate.Date,
            StartTime,
            CheckInOpen,
            CheckInClose,
            SelectedVenue?.Id,
            SelectedVenue?.Name ?? VenueQuery,
            SelectedFormat,
            Capacity,
            SelectedTeamCount,
            RsvpDeadline);

    private void ApplyState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    private static string FormatDate(DateTime date) =>
        date.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeSpan time) =>
        (DateTime.MinValue + time).ToString("h:mm tt", CultureInfo.InvariantCulture);
}
