using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class CreateSessionPageModelTests
{
    private static T? GetContractProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"ADMIN-4 requires CreateSessionPageModel.{propertyName}");
        return (T?)property!.GetValue(target);
    }

    private static void SetContractProperty<T>(object target, string propertyName, T? value)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"ADMIN-4 requires CreateSessionPageModel.{propertyName}");
        property!.CanWrite.Should().BeTrue($"ADMIN-4 requires CreateSessionPageModel.{propertyName} to be editable");
        property.SetValue(target, value);
    }
    private static CreateSessionPageModel SeedBackedModel(
        out SeedState state,
        out Mock<ISessionsNavigator> navigator)
    {
        state = new SeedState();
        navigator = new Mock<ISessionsNavigator>();
        return new CreateSessionPageModel(new SeedSessionAdminClient(state), navigator.Object);
    }

    private static CreateSessionPageModel MockBackedModel(
        out Mock<ISessionAdminClient> adminClient,
        out Mock<ISessionsNavigator> navigator)
    {
        adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.CreateSessionDefaults);
        adminClient
            .Setup(client => client.ListManagedSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        navigator = new Mock<ISessionsNavigator>();
        return new CreateSessionPageModel(adminClient.Object, navigator.Object);
    }

    private static CreateSessionDefaultsDto DeniedDefaults() =>
        new(
            CanManageSessions: false,
            DefaultGameDateLocal: new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Unspecified),
            DefaultStartTimeLocal: new TimeSpan(19, 40, 0),
            CheckInLeadMinutes: 10,
            CheckInCloseOffsetMinutes: 0,
            Formats: ["5v5", "7v7", "9v9"],
            DefaultFormatIndex: 1,
            DefaultCapacity: 20,
            MinimumCapacity: 1,
            MaximumCapacity: 40,
            TeamOptions: ["2 teams", "3 teams", "4 teams"],
            DefaultTeamIndex: 0,
            SavedVenue: new VenueDto(Guid.NewGuid(), "Marina Field", "South Bay", true),
            FeedLabel: "Team feed",
            DefaultRsvpDeadlineLocal: new TimeSpan(18, 40, 0));

    [Fact]
    public async Task Load_SeedDefaults_PopulatesFormAndEnablesPublish()
    {
        var pageModel = SeedBackedModel(out _, out _);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Formats.Should().Equal("5v5", "7v7", "9v9");
        pageModel.SelectedFormatIndex.Should().Be(1);
        pageModel.SelectedFormat.Should().Be("7v7");
        pageModel.Capacity.Should().Be(20);
        pageModel.SelectedVenue!.Name.Should().Be("Marina Field");
        pageModel.PreviewTitle.Should().Contain("Marina Field").And.Contain("Saturday pickup");
        pageModel.PreviewDateLabel.Should().Be("Jun 27");
        pageModel.PreviewTimeLabel.Should().Be("7:40 PM");
        pageModel.PreviewFormatLabel.Should().Contain("7v7").And.Contain("20 spots");
        pageModel.CheckInPreviewLabel.Should().Contain("Check-in 7:30 PM").And.Contain("closes 7:40 PM");
        GetContractProperty<TimeSpan>(pageModel, "RsvpCloseTime").Should().Be(new TimeSpan(18, 30, 0));
        pageModel.CanPublish.Should().BeTrue();
    }

    [Fact]
    public async Task Load_SeedDefaults_LoadsCreatedSessionsForAdminUpdates()
    {
        var pageModel = SeedBackedModel(out _, out _);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.HasManagedSessions.Should().BeTrue();
        pageModel.ManagedSessions.Should().Contain(session => session.Title.Contains("Marina Field"));
        pageModel.ManagedSessions.Should().OnlyContain(session => session.SessionId != Guid.Empty);
    }

    [Fact]
    public async Task EditManagedSession_LoadsExistingSessionIntoUpdateMode()
    {
        var pageModel = SeedBackedModel(out _, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);
        var session = pageModel.ManagedSessions.First(item => item.Title.Contains("Stanford Turf"));

        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        pageModel.IsEditingSession.Should().BeTrue();
        pageModel.EditingSessionId.Should().Be(session.SessionId);
        pageModel.PrimaryActionText.Should().Be("Update session");
        pageModel.SelectedVenue!.Name.Should().Be("Stanford Turf");
        pageModel.PreviewTitle.Should().Contain("Stanford Turf").And.Contain("Saturday pickup");
        pageModel.CanPublish.Should().BeTrue();
    }

    [Fact]
    public async Task EditManagedSession_ApiRequestException_ShowsServerMessage()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Test session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.Forbidden,
                "API request failed with status 403.",
                "Forbidden",
                "You do not have permission to edit this session."));
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        pageModel.ValidationMessage.Should().Be("You do not have permission to edit this session.");
    }

    private static ManagedSessionEditDto EditableSessionWithRsvpDeadlineDayBeforeGame(Guid sessionId, Guid venueId) =>
        new(
            sessionId,
            new CreateSessionCommand(
                new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Unspecified),
                new TimeSpan(19, 40, 0),
                new TimeSpan(19, 30, 0),
                new TimeSpan(19, 40, 0),
                venueId,
                "Marina Field",
                "7v7",
                20,
                2,
                new TimeSpan(20, 0, 0),
                RsvpDeadlineDayOffset: -1),
            IsPublished: true);

    [Fact]
    public async Task EditManagedSession_RsvpDeadlineOnDayBeforeGame_LoadsRsvpCloseDateOnThatDay()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var venueId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Test session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EditableSessionWithRsvpDeadlineDayBeforeGame(session.SessionId, venueId));
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        // Fix 2: a deadline stored the evening BEFORE the game must not be coerced onto game day when
        // the edit form loads.
        pageModel.GameDate.Should().Be(new DateTime(2026, 7, 11));
        GetContractProperty<DateTime>(pageModel, "RsvpCloseDate").Should().Be(new DateTime(2026, 7, 10));
    }

    [Fact]
    public async Task EditManagedSession_RsvpDeadlineOnDayBeforeGame_SaveWithoutFurtherEditsPreservesOffset()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var venueId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Test session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EditableSessionWithRsvpDeadlineDayBeforeGame(session.SessionId, venueId));
        CreateSessionCommand? captured = null;
        adminClient
            .Setup(client => client.UpdateSessionAsync(
                session.SessionId, It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CreateSessionCommand, CancellationToken>((_, command, _) => captured = command)
            .ReturnsAsync(CreateSessionResult.Success(session.SessionId));
        await pageModel.LoadCommand.ExecuteAsync(null);
        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        // Round-trip (b): loading then immediately saving must NOT drift the deadline onto game day.
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.GameDateLocal.Should().Be(new DateTime(2026, 7, 11));
        captured.RsvpDeadlineDayOffset.Should().Be(-1);
    }

    [Fact]
    public async Task EditManagedSession_GameDateMovedForward_RsvpCloseDateShiftsWithItPreservingOffset()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var venueId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Test session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EditableSessionWithRsvpDeadlineDayBeforeGame(session.SessionId, venueId));
        CreateSessionCommand? captured = null;
        adminClient
            .Setup(client => client.UpdateSessionAsync(
                session.SessionId, It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CreateSessionCommand, CancellationToken>((_, command, _) => captured = command)
            .ReturnsAsync(CreateSessionResult.Success(session.SessionId));
        await pageModel.LoadCommand.ExecuteAsync(null);
        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        // Round-trip (c): the admin postpones the game two days without manually re-touching the RSVP
        // close date — the offset (day before game) must shift with it, not reset onto the new game day.
        pageModel.GameDate = new DateTime(2026, 7, 13);
        GetContractProperty<DateTime>(pageModel, "RsvpCloseDate").Should().Be(new DateTime(2026, 7, 12));

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.GameDateLocal.Should().Be(new DateTime(2026, 7, 13));
        captured.RsvpDeadlineDayOffset.Should().Be(-1);
    }

    [Fact]
    public async Task Load_WithoutManagePermission_ShowsDeniedStateAndBlocksPublish()
    {
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedDefaults());
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(CreateSessionPageModel.DeniedTitle);
        pageModel.CanPublish.Should().BeFalse();
    }

    [Fact]
    public async Task Load_NetworkFailure_ShowsOfflineState()
    {
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(CreateSessionPageModel.OfflineTitle);
    }

    [Fact]
    public async Task Load_UnexpectedFailure_ShowsErrorState()
    {
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);

        var act = () => pageModel.LoadCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        pageModel.State.Should().Be(ViewState.Error);
    }

    [Fact]
    public async Task Load_ApiRequestException_ShowsServerMessageNotOfflineCopy()
    {
        // The server responded (it's an ApiRequestException, not a bare connectivity failure), so the
        // page should show its problem-details message rather than the "you're offline" copy.
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.InternalServerError,
                "API request failed with status 500.",
                "Unexpected error",
                "The session admin service is temporarily unavailable."));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateMessage.Should().Be("The session admin service is temporarily unavailable.");
        pageModel.StateMessage.Should().NotBe(CreateSessionPageModel.OfflineMessage);
    }

    [Fact]
    public async Task SearchVenues_WithQuery_PopulatesResults()
    {
        var pageModel = SeedBackedModel(out _, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.VenueQuery = "stanford";
        await pageModel.SearchVenuesCommand.ExecuteAsync(null);

        pageModel.HasVenueResults.Should().BeTrue();
        pageModel.VenueResults.Should().ContainSingle(venue => venue.Name == "Stanford Turf");
    }

    [Fact]
    public async Task SearchVenuesCommand_WithTypedVenue_SearchesApiClient()
    {
        var searched = default(string);
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.CreateSessionDefaults);
        adminClient
            .Setup(client => client.ListManagedSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        adminClient
            .Setup(client => client.SearchVenuesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string?, CancellationToken>((query, _) => searched = query)
            .ReturnsAsync([SeedFixtures.Venues.Single(venue => venue.Name == "Stanford Turf")]);
        var navigator = new Mock<ISessionsNavigator>();
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.SearchVenuesCommand.ExecuteAsync("stanford");

        searched.Should().Be("stanford");
        pageModel.VenueResults.Should().ContainSingle(venue => venue.Name == "Stanford Turf");
    }

    [Fact]
    public async Task SearchVenuesCommand_QueryMatchesSelectedVenueName_SkipsSearchAndKeepsListClosed()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);
        var venue = new VenueDto(Guid.NewGuid(), "Cuesta Park", "Mountain View", false);
        pageModel.SelectVenueCommand.Execute(venue);

        // Selecting a venue sets VenueQuery to its name, which the page's TextChanged handler turns
        // into exactly this call - it must not reopen the results list or hit the API.
        await pageModel.SearchVenuesCommand.ExecuteAsync(venue.Name);

        pageModel.VenueResults.Should().BeEmpty();
        pageModel.HasVenueResults.Should().BeFalse();
        adminClient.Verify(
            client => client.SearchVenuesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddVenue_WithUnknownTypedVenue_CreatesAndSelectsVenue()
    {
        var pageModel = SeedBackedModel(out _, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.VenueQuery = "New Park, Torrance";
        await pageModel.SearchVenuesCommand.ExecuteAsync(null);
        pageModel.CanCreateVenue.Should().BeTrue();

        await pageModel.AddVenueCommand.ExecuteAsync(null);

        pageModel.SelectedVenue.Should().NotBeNull();
        pageModel.SelectedVenue!.Name.Should().Be("New Park");
        pageModel.SelectedVenue.Locality.Should().Be("Torrance");
        pageModel.VenueQuery.Should().Be("New Park");
        pageModel.SavedVenueHint.Should().Be("Torrance");
        pageModel.CanPublish.Should().BeTrue();
    }

    [Fact]
    public async Task AddVenue_WithCommaOnlyLocalityTypedVenue_FallsBackToLocalityAsName()
    {
        var pageModel = SeedBackedModel(out _, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.VenueQuery = ", Torrance";
        await pageModel.SearchVenuesCommand.ExecuteAsync(null);
        pageModel.CanCreateVenue.Should().BeTrue();

        await pageModel.AddVenueCommand.ExecuteAsync(null);

        pageModel.SelectedVenue.Should().NotBeNull();
        pageModel.SelectedVenue!.Name.Should().Be("Torrance");
        pageModel.SelectedVenue.Locality.Should().Be("South Bay");
    }

    [Fact]
    public async Task AddVenue_ApiRequestException_ShowsServerMessage()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        adminClient
            .Setup(client => client.CreateVenueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.BadRequest,
                "API request failed with status 400.",
                "Validation failed",
                "A venue with that name already exists."));
        await pageModel.LoadCommand.ExecuteAsync(null);
        pageModel.VenueQuery = "New Park, Torrance";

        await pageModel.AddVenueCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("A venue with that name already exists.");
    }

    [Fact]
    public async Task AddVenue_StatusLessHttpRequestException_ShowsVenueOfflineMessage()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        adminClient
            .Setup(client => client.CreateVenueAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No network connection."));
        await pageModel.LoadCommand.ExecuteAsync(null);
        pageModel.VenueQuery = "New Park, Torrance";

        await pageModel.AddVenueCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be(CreateSessionPageModel.OfflineVenueMessage);
    }

    [Fact]
    public async Task SelectVenue_FromResults_SetsSelectionAndCollapsesList()
    {
        var pageModel = SeedBackedModel(out _, out _);
        await pageModel.LoadCommand.ExecuteAsync(null);
        var cuesta = SeedFixtures.Venues.Single(venue => venue.Name == "Cuesta Park");

        pageModel.SelectVenueCommand.Execute(cuesta);

        pageModel.SelectedVenue.Should().Be(cuesta);
        pageModel.VenueQuery.Should().Be("Cuesta Park");
        pageModel.SavedVenueHint.Should().Be(cuesta.Locality);
        pageModel.HasVenueResults.Should().BeFalse();
        pageModel.PreviewTitle.Should().Contain("Cuesta Park").And.Contain("Saturday pickup");
    }

    [Fact]
    public void EditingVenueQueryAfterSelection_ClearsSelection()
    {
        var pageModel = SeedBackedModel(out _, out _);
        pageModel.SelectVenueCommand.Execute(SeedFixtures.Venues[0]);

        pageModel.VenueQuery = "Marina Fi";

        pageModel.SelectedVenue.Should().BeNull();
        pageModel.HasSelectedVenue.Should().BeFalse();
    }

    [Fact]
    public async Task PublishToTeam_ValidForm_PublishesAndNavigatesToNewSession()
    {
        var pageModel = SeedBackedModel(out var state, out var navigator);
        var sessionsClient = new SeedSessionsClient(state);
        var publishedId = Guid.Empty;
        navigator
            .Setup(item => item.GoToSessionAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id => publishedId = id)
            .Returns(Task.CompletedTask);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        publishedId.Should().NotBe(Guid.Empty);
        pageModel.ValidationMessage.Should().BeEmpty();
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);
        dashboard.ComingUpSessions.Should().ContainSingle(session => session.Id == publishedId);
        navigator.Verify(item => item.GoToSessionAsync(publishedId), Times.Once);
    }

    [Fact]
    public async Task PublishToTeam_TappedTwice_DoesNotCreateDuplicateSession()
    {
        var pageModel = SeedBackedModel(out var state, out var navigator);
        var sessionsClient = new SeedSessionsClient(state);
        var ids = new List<Guid>();
        navigator
            .Setup(item => item.GoToSessionAsync(It.IsAny<Guid>()))
            .Callback<Guid>(ids.Add)
            .Returns(Task.CompletedTask);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        ids.Should().HaveCount(2);
        ids.Should().OnlyContain(id => id == ids[0]);
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);
        dashboard.ComingUpSessions.Count(session => session.Id == ids[0]).Should().Be(1);
    }

    [Fact]
    public async Task PublishToTeam_EditingExistingSession_UpdatesSessionWithoutCreatingDuplicate()
    {
        var pageModel = SeedBackedModel(out var state, out var navigator);
        var sessionsClient = new SeedSessionsClient(state);
        await pageModel.LoadCommand.ExecuteAsync(null);
        var session = pageModel.ManagedSessions.First(item => item.Title.Contains("Marina Field"));
        var originalDashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);
        var originalCount = originalDashboard.ComingUpSessions.Count;

        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);
        pageModel.Capacity = 18;
        pageModel.SelectVenueCommand.Execute(SeedFixtures.Venues.Single(venue => venue.Name == "Cuesta Park"));
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        var updated = await sessionsClient.GetSessionAsync(session.SessionId, CancellationToken.None);
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);

        updated!.Venue.Should().Be("Cuesta Park");
        updated.Capacity.Should().Be(18);
        dashboard.ComingUpSessions.Count.Should().Be(originalCount);
        pageModel.ValidationMessage.Should().BeEmpty();
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_RetryAfterPublishFailure_ReusesDraftWithoutDuplicating()
    {
        var draftId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.CreateSessionDefaults);
        adminClient
            .Setup(client => client.CreateDraftAsync(
                It.IsAny<CreateSessionCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSessionResult.Success(draftId));
        adminClient
            .SetupSequence(client => client.PublishAsync(draftId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"))
            .ReturnsAsync(CreateSessionResult.Success(sessionId));
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToSessionAsync(sessionId))
            .Returns(Task.CompletedTask);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        adminClient.Verify(
            client => client.CreateDraftAsync(
                It.IsAny<CreateSessionCommand>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        navigator.Verify(item => item.GoToSessionAsync(sessionId), Times.Once);
        pageModel.ValidationMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishToTeam_ApiRequestException_ShowsServerMessageNotOfflineCopy()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        adminClient
            .Setup(client => client.CreateDraftAsync(It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.InternalServerError,
                "API request failed with status 500.",
                "Unexpected error",
                "The session admin service hit an unexpected error."));
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("The session admin service hit an unexpected error.");
        pageModel.ValidationMessage.Should().NotBe(CreateSessionPageModel.OfflinePublishMessage);
    }

    [Fact]
    public async Task PublishToTeam_StatusLessHttpRequestException_ShowsOfflineMessage()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        adminClient
            .Setup(client => client.CreateDraftAsync(It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No network connection."));
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be(CreateSessionPageModel.OfflinePublishMessage);
    }

    [Fact]
    public async Task PublishToTeam_EditingSessionApiRequestException_ShowsServerMessageNotOfflineCopy()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var venueId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Test session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        var editable = new ManagedSessionEditDto(
            session.SessionId,
            new CreateSessionCommand(
                new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Unspecified),
                new TimeSpan(19, 40, 0),
                new TimeSpan(19, 30, 0),
                new TimeSpan(19, 40, 0),
                venueId,
                "Marina Field",
                "7v7",
                20,
                2,
                new TimeSpan(18, 30, 0)),
            IsPublished: true);
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(editable);
        adminClient
            .Setup(client => client.UpdateSessionAsync(
                session.SessionId, It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.Conflict,
                "API request failed with status 409.",
                "Conflict",
                "This session was already updated."));
        await pageModel.LoadCommand.ExecuteAsync(null);
        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("This session was already updated.");
        pageModel.ValidationMessage.Should().NotBe(CreateSessionPageModel.OfflinePublishMessage);
    }

    [Fact]
    public async Task PublishToTeam_MissingVenue_ShowsValidationAndDoesNotPublish()
    {
        var pageModel = SeedBackedModel(out var state, out var navigator);
        var sessionsClient = new SeedSessionsClient(state);
        await pageModel.LoadCommand.ExecuteAsync(null);

        // Clear the preselected venue the way editing the search field would.
        pageModel.VenueQuery = string.Empty;

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Choose a venue to continue.");
        var dashboard = await sessionsClient.GetDashboardAsync(CancellationToken.None);
        dashboard.ComingUpSessions.Should().OnlyContain(session => session.Capacity == 20);
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_ValidForm_SendsAdjustedCheckInWindowAndRsvpDeadline()
    {
        var captured = default(CreateSessionCommand);
        var draftId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var adminClient = new Mock<ISessionAdminClient>();
        adminClient
            .Setup(client => client.GetDefaultsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.CreateSessionDefaults);
        adminClient
            .Setup(client => client.CreateDraftAsync(
                It.IsAny<CreateSessionCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateSessionCommand, CancellationToken>((command, _) => captured = command)
            .ReturnsAsync(CreateSessionResult.Success(draftId));
        adminClient
            .Setup(client => client.PublishAsync(draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSessionResult.Success(sessionId));
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToSessionAsync(sessionId))
            .Returns(Task.CompletedTask);
        var pageModel = new CreateSessionPageModel(adminClient.Object, navigator.Object);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.CheckInOpen = new TimeSpan(19, 25, 0);
        pageModel.CheckInClose = new TimeSpan(19, 40, 0);
        SetContractProperty<DateTime>(pageModel, "RsvpCloseDate", pageModel.GameDate.Date);
        SetContractProperty<TimeSpan>(pageModel, "RsvpCloseTime", new TimeSpan(18, 30, 0));
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.CheckInOpenLocal.Should().Be(new TimeSpan(19, 25, 0));
        captured.CheckInCloseLocal.Should().Be(new TimeSpan(19, 40, 0));
        captured.RsvpDeadlineLocal.Should().Be(new TimeSpan(18, 30, 0));
    }

    [Fact]
    public async Task PublishToTeam_MissingRsvpDeadline_ShowsValidationAndDoesNotPublish()
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        SetContractProperty<DateTime>(pageModel, "RsvpCloseDate", default);
        SetContractProperty<TimeSpan>(pageModel, "RsvpCloseTime", default);
        pageModel.CanPublish.Should().BeFalse();
        pageModel.PublishToTeamCommand.CanExecute(null).Should().BeFalse();
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_RsvpDeadlineAfterSessionStart_ShowsValidationAndDoesNotPublish()
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        SetContractProperty<DateTime>(pageModel, "RsvpCloseDate", pageModel.GameDate.Date);
        SetContractProperty<TimeSpan>(pageModel, "RsvpCloseTime", pageModel.StartTime.Add(TimeSpan.FromMinutes(1)));
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Contain("RSVP").And.Contain("after session start");
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task PublishToTeam_InvalidTeamSetup_ShowsValidationAndDoesNotPublish(int selectedTeamIndex)
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.SelectedTeamIndex = selectedTeamIndex;
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Select a team setup.");
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_CapacityLessThanOne_ShowsPositiveCapacityValidation()
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.Capacity = 0;
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Capacity must be at least 1.");
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_AdjustedCheckInCloseBeforeOpen_ShowsValidationAndDoesNotPublish()
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.CheckInOpen = new TimeSpan(19, 45, 0);
        pageModel.CheckInClose = new TimeSpan(19, 40, 0);
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Check-in must open before it closes.");
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PublishToTeam_CheckInCloseAfterStart_ShowsValidationAndDoesNotPublish()
    {
        var pageModel = SeedBackedModel(out _, out var navigator);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.CheckInOpen = new TimeSpan(19, 30, 0);
        pageModel.CheckInClose = new TimeSpan(19, 45, 0);

        // The rule must surface via Validate() on tap rather than silently disable the button - the
        // button (and the command it drives) must stay enabled so tapping reaches Validate() at all.
        pageModel.CanPublish.Should().BeTrue();
        pageModel.PublishToTeamCommand.CanExecute(null).Should().BeTrue();
        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Check-in close cannot be after session start.");
        navigator.Verify(item => item.GoToSessionAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task EditManagedSession_LegacyCheckInCloseAfterStart_KeepsUpdateButtonTappableAndSurfacesValidation()
    {
        var pageModel = MockBackedModel(out var adminClient, out _);
        var session = new ManagedSessionDto(
            Guid.NewGuid(), "Legacy session", "Jul 11", "7:40 PM", "Marina Field", "7v7", 20, "Published");
        var editable = new ManagedSessionEditDto(
            session.SessionId,
            new CreateSessionCommand(
                new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Unspecified),
                new TimeSpan(19, 40, 0),
                new TimeSpan(19, 30, 0),
                new TimeSpan(19, 45, 0), // stale seed offset: check-in close after session start
                Guid.NewGuid(),
                "Marina Field",
                "7v7",
                20,
                2,
                new TimeSpan(18, 30, 0)),
            IsPublished: true);
        adminClient
            .Setup(client => client.GetSessionForEditAsync(session.SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(editable);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.EditManagedSessionCommand.ExecuteAsync(session);

        pageModel.CanPublish.Should().BeTrue("the admin must be able to discover why the update is blocked");
        pageModel.PublishToTeamCommand.CanExecute(null).Should().BeTrue();

        await pageModel.PublishToTeamCommand.ExecuteAsync(null);

        pageModel.ValidationMessage.Should().Be("Check-in close cannot be after session start.");
        adminClient.Verify(
            client => client.UpdateSessionAsync(
                It.IsAny<Guid>(), It.IsAny<CreateSessionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

