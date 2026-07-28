using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Domain.Entities.Announcements;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Announcements;

public sealed class AnnouncementHandlerTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 27, 17, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAnnouncement_WhenAdminBelongsToGroup_SnapshotsRecipientCountAtSendTime()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew", WhatsAppMemberCount = 24 };
        var announcements = new Mock<IAnnouncementRepository>();
        Announcement? saved = null;
        announcements
            .Setup(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Callback<Announcement, CancellationToken>((announcement, _) => saved = announcement)
            .Returns(Task.CompletedTask);

        var handler = new PostAnnouncementCommandHandler(
            new PostAnnouncementCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            new Mock<IUnitOfWork>().Object,
            Clock().Object);

        var result = await handler.HandleAsync(
            new PostAnnouncementCommand(groupChat.Id, "  Pitch change: Baylands Field.  ", SendPush: true));

        saved.Should().NotBeNull();
        saved!.Body.Should().Be("Pitch change: Baylands Field.", because: "the body is trimmed before it is broadcast");
        saved.SentAtUtc.Should().Be(NowUtc, because: "send time comes from IClock, never DateTime.Now");
        saved.RecipientCount.Should().Be(24, because: "the audience size is snapshotted so later joins cannot rewrite history");
        saved.PushRequested.Should().BeTrue();
        result.ReadCount.Should().Be(0);
        result.RecipientCount.Should().Be(24);
    }

    [Fact]
    public async Task PostAnnouncement_WhenAdminIsNotInThatGroup_IsRejectedAsNotFound()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var foreignGroupId = Guid.NewGuid();
        var announcements = new Mock<IAnnouncementRepository>(MockBehavior.Strict);

        var handler = new PostAnnouncementCommandHandler(
            new PostAnnouncementCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, foreignGroupId, isMember: false).Object,
            new Mock<IGroupChatRepository>(MockBehavior.Strict).Object,
            announcements.Object,
            new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            Clock().Object);

        var act = () => handler.HandleAsync(new PostAnnouncementCommand(foreignGroupId, "Hello", SendPush: false));

        await act.Should().ThrowAsync<ApplicationNotFoundException>(
            because: "an admin may broadcast only to their own group, and a group they cannot see must not be confirmed to exist");
        announcements.Verify(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostAnnouncement_WhenBodyIsBlank_FailsValidation(string body)
    {
        var handler = CreatePostHandler(out _, out _);

        var act = () => handler.HandleAsync(new PostAnnouncementCommand(Guid.NewGuid(), body, SendPush: false));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task PostAnnouncement_WhenBodyExceedsFiveHundredCharacters_FailsValidation()
    {
        var handler = CreatePostHandler(out _, out _);

        var act = () => handler.HandleAsync(
            new PostAnnouncementCommand(Guid.NewGuid(), new string('a', 501), SendPush: false));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task PostAnnouncement_WhenBodyIsExactlyFiveHundredCharacters_IsAccepted()
    {
        var handler = CreatePostHandler(out var groupChat, out var announcements);

        await handler.HandleAsync(new PostAnnouncementCommand(groupChat.Id, new string('a', 500), SendPush: false));

        announcements.Verify(
            x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "500 characters is the inclusive limit the composer counts against");
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenPageIsFull_ReturnsCursorFromOldestItem()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };
        var author = Guid.NewGuid();

        // Three rows returned for a limit of two: the extra row is the "more history exists" signal.
        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ReadModel(groupChat, author, NowUtc),
                ReadModel(groupChat, author, NowUtc.AddHours(-1)),
                ReadModel(groupChat, author, NowUtc.AddHours(-2)),
            ]);
        announcements
            .Setup(x => x.CountUnreadForGroupAsync(profile.Id, groupChat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(null, profile.Id, groupChat.Id).Object);

        var result = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 2));

        result.Announcements.Should().HaveCount(2, because: "the extra row is a look-ahead, not page content");
        result.NextCursorUtc.Should().Be(NowUtc.AddHours(-1), because: "the cursor is the oldest item actually returned");
        result.UnreadCount.Should().Be(2);
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenHistoryIsExhausted_ReturnsNullCursor()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 21, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ReadModel(groupChat, Guid.NewGuid(), NowUtc)]);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(null, profile.Id, groupChat.Id).Object);

        var result = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 20));

        result.NextCursorUtc.Should().BeNull(because: "a short page means there is nothing left to page to");
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenReadMarkIsAfterSendTime_MarksAnnouncementRead()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };
        var otherAdmin = Guid.NewGuid();

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ReadModel(groupChat, otherAdmin, NowUtc),
                ReadModel(groupChat, otherAdmin, NowUtc.AddDays(-1)),
                ReadModel(groupChat, profile.Id, NowUtc.AddDays(-2)),
            ]);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(NowUtc.AddHours(-12), profile.Id, groupChat.Id).Object);

        var result = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 20));

        result.Announcements[0].IsUnread.Should().BeTrue(because: "it was sent after the read mark");
        result.Announcements[1].IsUnread.Should().BeFalse(because: "it was sent before the read mark");
        result.Announcements[2].IsUnread.Should().BeFalse(because: "a player is never notified of their own broadcast");
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenPlayerIsNotInGroup_IsRejectedAsNotFound()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var foreignGroupId = Guid.NewGuid();
        var announcements = new Mock<IAnnouncementRepository>(MockBehavior.Strict);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, foreignGroupId, isMember: false).Object,
            new Mock<IGroupChatRepository>(MockBehavior.Strict).Object,
            announcements.Object,
            new Mock<IGroupAnnouncementReadMarkerRepository>(MockBehavior.Strict).Object);

        var act = () => handler.HandleAsync(new GetGroupAnnouncementsQuery(foreignGroupId, null, null, 20));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
        announcements.Verify(
            x => x.ListForGroupAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "membership is checked before any announcement is read");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task GetGroupAnnouncements_WhenLimitIsOutOfRange_FailsValidation(int limit)
    {
        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            new Mock<ICurrentUser>().Object,
            new Mock<IPlayerProfileRepository>().Object,
            new Mock<IPlayerGroupLinkRepository>().Object,
            new Mock<IGroupChatRepository>().Object,
            new Mock<IAnnouncementRepository>().Object,
            new Mock<IGroupAnnouncementReadMarkerRepository>().Object);

        var act = () => handler.HandleAsync(new GetGroupAnnouncementsQuery(Guid.NewGuid(), null, null, limit));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task MarkRead_WhenNoMarkExists_CreatesOneAtTheCurrentTime()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>();
        markers
            .Setup(x => x.FindAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GroupAnnouncementReadMarker?)null);
        GroupAnnouncementReadMarker? added = null;
        markers
            .Setup(x => x.AddAsync(It.IsAny<GroupAnnouncementReadMarker>(), It.IsAny<CancellationToken>()))
            .Callback<GroupAnnouncementReadMarker, CancellationToken>((marker, _) => added = marker)
            .Returns(Task.CompletedTask);

        var handler = CreateMarkReadHandler(identityUserId, profile, groupChatId, markers);

        var result = await handler.HandleAsync(new MarkGroupAnnouncementsReadCommand(groupChatId));

        added.Should().NotBeNull();
        added!.LastReadAtUtc.Should().Be(NowUtc);
        result.ReadThroughUtc.Should().Be(NowUtc);
        result.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task MarkRead_WhenExistingMarkIsNewerThanNow_LeavesTheMarkAlone()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var future = NowUtc.AddMinutes(5);
        var marker = new GroupAnnouncementReadMarker
        {
            PlayerProfileId = profile.Id,
            GroupChatId = groupChatId,
            LastReadAtUtc = future,
        };
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>();
        markers
            .Setup(x => x.FindAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marker);

        var handler = CreateMarkReadHandler(identityUserId, profile, groupChatId, markers);

        var result = await handler.HandleAsync(new MarkGroupAnnouncementsReadCommand(groupChatId));

        marker.LastReadAtUtc.Should().Be(future, because: "the read mark only ever moves forward");
        result.ReadThroughUtc.Should().Be(future);
        markers.Verify(x => x.AddAsync(It.IsAny<GroupAnnouncementReadMarker>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSentAnnouncements_WhenAdminHasNoGroups_ReturnsEmptyWithoutQuerying()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var links = new Mock<IPlayerGroupLinkRepository>();
        links
            .Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var announcements = new Mock<IAnnouncementRepository>(MockBehavior.Strict);

        var handler = new GetSentAnnouncementsQueryHandler(
            new GetSentAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            links.Object,
            announcements.Object);

        var result = await handler.HandleAsync(new GetSentAnnouncementsQuery(10));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSentAnnouncements_WhenAdminHasGroups_ScopesTheQueryToThoseGroups()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var links = new Mock<IPlayerGroupLinkRepository>();
        links
            .Setup(x => x.ListByPlayerAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupChatId }]);
        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListSentByAuthorAsync(
                profile.Id,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(groupChatId)),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SentAnnouncementReadModel(
                    Guid.NewGuid(), groupChatId, "Saturday crew", "Shirts", NowUtc, RecipientCount: 24, ReadCount: 18),
            ]);

        var handler = new GetSentAnnouncementsQueryHandler(
            new GetSentAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            links.Object,
            announcements.Object);

        var result = await handler.HandleAsync(new GetSentAnnouncementsQuery(10));

        result.Should().ContainSingle();
        result[0].ReadCount.Should().Be(18);
        result[0].RecipientCount.Should().Be(24);
    }

    [Fact]
    public async Task GetUnreadCount_WhenCallerIsUnauthenticated_IsRejected()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);

        var handler = new GetUnreadAnnouncementCountQueryHandler(
            currentUser.Object,
            new Mock<IPlayerProfileRepository>().Object,
            new Mock<IAnnouncementRepository>(MockBehavior.Strict).Object);

        var act = () => handler.HandleAsync(new GetUnreadAnnouncementCountQuery());

        await act.Should().ThrowAsync<ApplicationUnauthenticatedException>();
    }


    [Fact]
    public async Task GetGroupAnnouncements_WhenTwoAnnouncementsShareASendTime_TheOlderOneIsStillReachable()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };
        var author = Guid.NewGuid();

        // All three sent on the same tick — a bulk import or a fixed clock produces exactly this.
        var ordered = new[]
            {
                ReadModel(groupChat, author, NowUtc),
                ReadModel(groupChat, author, NowUtc),
                ReadModel(groupChat, author, NowUtc),
            }
            .OrderByDescending(x => x.SentAtUtc)
            .ThenByDescending(x => x.Id)
            .ToArray();

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordered);
        announcements
            .Setup(x => x.CountUnreadForGroupAsync(profile.Id, groupChat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(null, profile.Id, groupChat.Id).Object);

        var page = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 2));

        page.NextCursorUtc.Should().Be(NowUtc);
        page.NextCursorId.Should().Be(
            ordered[1].Id,
            because: "a timestamp alone cannot order tied rows, so the cursor must carry the id too");

        // The next page must ask for rows strictly after the composite cursor, not just the time.
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, NowUtc, ordered[1].Id, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ordered[2]]);

        var nextPage = await handler.HandleAsync(
            new GetGroupAnnouncementsQuery(groupChat.Id, page.NextCursorUtc, page.NextCursorId, 2));

        nextPage.Announcements.Should().ContainSingle()
            .Which.Id.Should().Be(ordered[2].Id, because: "no announcement may be unreachable by any page");
        nextPage.NextCursorUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenSentExactlyAtTheReadMark_CountsAsRead()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 21, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ReadModel(groupChat, Guid.NewGuid(), NowUtc)]);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(NowUtc, profile.Id, groupChat.Id).Object);

        var result = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 20));

        result.Announcements[0].IsUnread.Should().BeFalse(
            because: "SentAtUtc == LastReadAtUtc must mean read, consistently with the repository counts");
    }

    [Fact]
    public async Task GetGroupAnnouncements_WhenSentBeforeThePlayerJoined_IsNotUnread()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew" };
        var joinedAtUtc = NowUtc.AddDays(-1);

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.ListForGroupAsync(groupChat.Id, null, null, 21, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                ReadModel(groupChat, Guid.NewGuid(), NowUtc),
                ReadModel(groupChat, Guid.NewGuid(), NowUtc.AddDays(-30)),
            ]);

        var handler = new GetGroupAnnouncementsQueryHandler(
            new GetGroupAnnouncementsQueryValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true, joinedAtUtc).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            ReadMarkers(null, profile.Id, groupChat.Id).Object);

        var result = await handler.HandleAsync(new GetGroupAnnouncementsQuery(groupChat.Id, null, null, 20));

        result.Announcements[0].IsUnread.Should().BeTrue();
        result.Announcements[1].IsUnread.Should().BeFalse(
            because: "joining a group must not hand the new member its entire back catalogue as unread");
    }

    [Fact]
    public async Task PostAnnouncement_WhenSnapshottingRecipients_CountsLinkedMembersExcludingTheAuthor()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew", WhatsAppMemberCount = 8 };
        var links = new Mock<IPlayerGroupLinkRepository>();
        links
            .Setup(x => x.FindLinkAsync(profile.Id, groupChat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerGroupLink { PlayerProfileId = profile.Id, GroupChatId = groupChat.Id });
        links
            .Setup(x => x.CountMembersAsync(groupChat.Id, profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);
        var announcements = new Mock<IAnnouncementRepository>();
        Announcement? saved = null;
        announcements
            .Setup(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
            .Callback<Announcement, CancellationToken>((announcement, _) => saved = announcement)
            .Returns(Task.CompletedTask);

        var handler = new PostAnnouncementCommandHandler(
            new PostAnnouncementCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            links.Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            new Mock<IUnitOfWork>().Object,
            Clock().Object);

        await handler.HandleAsync(new PostAnnouncementCommand(groupChat.Id, "Kickoff moved", SendPush: false));

        saved!.RecipientCount.Should().Be(
            12,
            because: "the receipt denominator must count app members who can receive it, not the WhatsApp roster, "
                + "otherwise the read count can exceed it");
    }

    [Fact]
    public async Task MarkRead_WhenGroupHasAnnouncements_MarksThroughTheNewestSendTimeNotTheClock()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var newestSentAtUtc = NowUtc.AddMinutes(-3);
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>();
        markers
            .Setup(x => x.FindAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GroupAnnouncementReadMarker?)null);
        GroupAnnouncementReadMarker? added = null;
        markers
            .Setup(x => x.AddAsync(It.IsAny<GroupAnnouncementReadMarker>(), It.IsAny<CancellationToken>()))
            .Callback<GroupAnnouncementReadMarker, CancellationToken>((marker, _) => added = marker)
            .Returns(Task.CompletedTask);

        var handler = CreateMarkReadHandler(identityUserId, profile, groupChatId, markers, newestSentAtUtc);

        var result = await handler.HandleAsync(new MarkGroupAnnouncementsReadCommand(groupChatId));

        added!.LastReadAtUtc.Should().Be(
            newestSentAtUtc,
            because: "a wall clock can run ahead of an announcement that is stamped but not yet committed, "
                + "which would mark an unseen broadcast read forever");
        result.ReadThroughUtc.Should().Be(newestSentAtUtc);
    }

    [Fact]
    public async Task MarkRead_WhenAnOlderMarkExists_AdvancesIt()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var marker = new GroupAnnouncementReadMarker
        {
            PlayerProfileId = profile.Id,
            GroupChatId = groupChatId,
            LastReadAtUtc = NowUtc.AddDays(-2),
        };
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>();
        markers
            .Setup(x => x.FindAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marker);

        var handler = CreateMarkReadHandler(identityUserId, profile, groupChatId, markers, NowUtc);

        var result = await handler.HandleAsync(new MarkGroupAnnouncementsReadCommand(groupChatId));

        marker.LastReadAtUtc.Should().Be(NowUtc);
        result.ReadThroughUtc.Should().Be(NowUtc);
        markers.Verify(x => x.Update(marker), Times.Once);
    }

    [Fact]
    public async Task MarkRead_WhenTwoFirstReadsRace_RecoversInsteadOfFailingTheCaller()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var groupChatId = Guid.NewGuid();
        var winner = new GroupAnnouncementReadMarker
        {
            PlayerProfileId = profile.Id,
            GroupChatId = groupChatId,
            LastReadAtUtc = NowUtc.AddMinutes(-10),
        };

        // First look-up sees no mark; the competing request commits one before we save, so our
        // insert violates the unique index. The retry must find the winner's row and advance it.
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>();
        markers
            .SetupSequence(x => x.FindAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GroupAnnouncementReadMarker?)null)
            .ReturnsAsync(winner);
        markers
            .Setup(x => x.AddAsync(It.IsAny<GroupAnnouncementReadMarker>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .SetupSequence(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("duplicate key"))
            .ReturnsAsync(1);

        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.FindLatestSentAtUtcAsync(groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NowUtc);
        announcements
            .Setup(x => x.CountUnreadForGroupAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new MarkGroupAnnouncementsReadCommandHandler(
            new MarkGroupAnnouncementsReadCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChatId, isMember: true).Object,
            markers.Object,
            announcements.Object,
            unitOfWork.Object,
            Clock().Object);

        var result = await handler.HandleAsync(new MarkGroupAnnouncementsReadCommand(groupChatId));

        result.ReadThroughUtc.Should().Be(
            NowUtc,
            because: "an endpoint documented as safe to repeat must not surface a concurrent tap as a conflict");
        winner.LastReadAtUtc.Should().Be(NowUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public async Task GetSentAnnouncements_WhenLimitIsOutOfRange_FailsValidation(int limit)
    {
        var handler = new GetSentAnnouncementsQueryHandler(
            new GetSentAnnouncementsQueryValidator(),
            new Mock<ICurrentUser>().Object,
            new Mock<IPlayerProfileRepository>().Object,
            new Mock<IPlayerGroupLinkRepository>().Object,
            new Mock<IAnnouncementRepository>().Object);

        var act = () => handler.HandleAsync(new GetSentAnnouncementsQuery(limit));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task PostAnnouncement_WhenBodyIsFiveHundredCharactersPlusWhitespace_IsAccepted()
    {
        var handler = CreatePostHandler(out var groupChat, out var announcements);

        await handler.HandleAsync(
            new PostAnnouncementCommand(groupChat.Id, "  " + new string('a', 500) + "  ", SendPush: false));

        announcements.Verify(
            x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the limit applies to the trimmed text that actually gets stored");
    }

    private static PostAnnouncementCommandHandler CreatePostHandler(
        out GroupChat groupChat,
        out Mock<IAnnouncementRepository> announcements)
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        groupChat = new GroupChat { Id = Guid.NewGuid(), GroupName = "Saturday crew", WhatsAppMemberCount = 24 };
        announcements = new Mock<IAnnouncementRepository>();

        return new PostAnnouncementCommandHandler(
            new PostAnnouncementCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChat.Id, isMember: true).Object,
            GroupChats(groupChat).Object,
            announcements.Object,
            new Mock<IUnitOfWork>().Object,
            Clock().Object);
    }

    private static MarkGroupAnnouncementsReadCommandHandler CreateMarkReadHandler(
        Guid identityUserId,
        PlayerProfile profile,
        Guid groupChatId,
        Mock<IGroupAnnouncementReadMarkerRepository> markers,
        DateTime? newestSentAtUtc = null)
    {
        var announcements = new Mock<IAnnouncementRepository>();
        announcements
            .Setup(x => x.CountUnreadForGroupAsync(profile.Id, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        announcements
            .Setup(x => x.FindLatestSentAtUtcAsync(groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newestSentAtUtc);

        return new MarkGroupAnnouncementsReadCommandHandler(
            new MarkGroupAnnouncementsReadCommandValidator(),
            CurrentUser(identityUserId).Object,
            Profiles(identityUserId, profile).Object,
            GroupLinks(profile.Id, groupChatId, isMember: true).Object,
            markers.Object,
            announcements.Object,
            new Mock<IUnitOfWork>().Object,
            Clock().Object);
    }

    private static AnnouncementReadModel ReadModel(GroupChat groupChat, Guid authorId, DateTime sentAtUtc) =>
        new(Guid.NewGuid(), groupChat.Id, groupChat.GroupName, authorId, "Ayo", "Body", sentAtUtc);

    private static Mock<ICurrentUser> CurrentUser(Guid identityUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        return currentUser;
    }

    private static Mock<IPlayerProfileRepository> Profiles(Guid identityUserId, PlayerProfile profile)
    {
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles
            .Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return profiles;
    }

    private static Mock<IPlayerGroupLinkRepository> GroupLinks(
        Guid playerProfileId,
        Guid groupChatId,
        bool isMember,
        DateTime? joinedAtUtc = null)
    {
        var links = new Mock<IPlayerGroupLinkRepository>();
        links
            .Setup(x => x.FindLinkAsync(playerProfileId, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isMember
                ? new PlayerGroupLink
                {
                    PlayerProfileId = playerProfileId,
                    GroupChatId = groupChatId,
                    CreatedAt = joinedAtUtc ?? NowUtc.AddYears(-1),
                }
                : null);
        links
            .Setup(x => x.CountMembersAsync(groupChatId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(24);
        return links;
    }

    private static Mock<IGroupChatRepository> GroupChats(GroupChat groupChat)
    {
        var groupChats = new Mock<IGroupChatRepository>();
        groupChats
            .Setup(x => x.GetByIdAsync(groupChat.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(groupChat);
        return groupChats;
    }

    private static Mock<IGroupAnnouncementReadMarkerRepository> ReadMarkers(
        DateTime? lastReadAtUtc,
        Guid playerProfileId,
        Guid groupChatId)
    {
        var markers = new Mock<IGroupAnnouncementReadMarkerRepository>(MockBehavior.Strict);
        markers
            .Setup(x => x.FindAsync(playerProfileId, groupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastReadAtUtc is null
                ? null
                : new GroupAnnouncementReadMarker
                {
                    PlayerProfileId = playerProfileId,
                    GroupChatId = groupChatId,
                    LastReadAtUtc = lastReadAtUtc.Value,
                });
        return markers;
    }

    private static Mock<IClock> Clock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
        return clock;
    }
}
