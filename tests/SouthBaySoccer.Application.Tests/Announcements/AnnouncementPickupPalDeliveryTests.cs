using System.Text.Json;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Application.Features.Outbox;
using SouthBaySoccer.Domain.Entities.Announcements;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Announcements;

public sealed class AnnouncementPickupPalDeliveryTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_WhenPosting_CommitsLocalPostAndLeasedIntentBeforeSendingExactBody()
    {
        var fixture = new Fixture();
        var events = new List<string>();
        fixture.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (events.Count == 0)
                {
                    fixture.AddedAnnouncement.Should().NotBeNull();
                    fixture.AddedDelivery.Should().NotBeNull();
                    var delivery = fixture.AddedDelivery!; // Asserted above.
                    delivery.Status.Should().Be(OutboxMessageStatus.Processing);
                    delivery.LockToken.Should().NotBeNullOrEmpty();
                    delivery.LockedUntilUtc.Should().BeAfter(NowUtc);
                    using var payload = JsonDocument.Parse(delivery.PayloadJson);
                    payload.RootElement.GetProperty("AnnouncementId").GetGuid().Should().Be(fixture.AddedAnnouncement!.Id);
                    payload.RootElement.EnumerateObject().Should().ContainSingle();
                }
                events.Add("save");
            }).ReturnsAsync(2);
        fixture.Client.Setup(x => x.SendAsync(fixture.Group.ExternalId, "Pitch changed", It.IsAny<CancellationToken>()))
            .Callback(() => events.Add("send")).ReturnsAsync(PickupPalAnnouncementSendResult.Sent);

        var result = await fixture.PostAsync();

        events.Should().Equal("save", "send", "save");
        result.Body.Should().Be("Pitch changed");
        fixture.AddedDelivery!.Status.Should().Be(OutboxMessageStatus.Processed); // Captured at commit.
        fixture.AddedDelivery.ProcessedAtUtc.Should().Be(NowUtc);
        fixture.AddedDelivery.LockToken.Should().BeNull();
        fixture.Client.VerifyAll();
    }

    [Fact]
    public async Task HandleAsync_WhenInitialCommitFails_DoesNotSend()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));

        var act = () => fixture.PostAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PickupPalAnnouncementSendResult.Retry, OutboxMessageStatus.RetryScheduled)]
    [InlineData(PickupPalAnnouncementSendResult.Rejected, OutboxMessageStatus.DeadLettered)]
    public async Task HandleAsync_WhenProviderCannotSend_PersistsOutcomeAndReturnsLocalPost(
        PickupPalAnnouncementSendResult outcome, OutboxMessageStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Client.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var result = await fixture.PostAsync();

        result.Id.Should().Be(fixture.AddedAnnouncement!.Id); // Captured by AddAsync.
        fixture.Outbox.Verify(x => x.Update(It.Is<OutboxMessage>(m => m.Status == expectedStatus
            && m.AttemptCount == 1 && m.LockToken == null && m.LockedUntilUtc == null)), Times.Once);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_WhenProviderOrSettlementThrows_ReturnsCommittedLocalPostAndRetainsDurableIntent(bool settlementFails)
    {
        var fixture = new Fixture();
        string? committedIntent = null;
        var saves = 0;
        fixture.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++saves == 1)
                {
                    committedIntent = JsonSerializer.Serialize(fixture.AddedDelivery);
                    return Task.FromResult(2);
                }
                return Task.FromException<int>(new InvalidOperationException("Settlement failed"));
            });
        var send = fixture.Client.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (settlementFails)
            send.ReturnsAsync(PickupPalAnnouncementSendResult.Sent);
        else
            send.ThrowsAsync(new HttpRequestException("Provider unavailable"));

        var result = await fixture.PostAsync();

        result.Id.Should().Be(fixture.AddedAnnouncement!.Id); // Captured by AddAsync.
        var persisted = JsonSerializer.Deserialize<OutboxMessage>(committedIntent!); // First commit must run before send.
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(OutboxMessageStatus.Processing); // Asserted above.
        persisted.LockedUntilUtc.Should().BeAfter(NowUtc);
        fixture.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
        fixture.Announcements.Verify(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerIsNotGroupAdmin_DoesNotQueueOrSend()
    {
        var fixture = new Fixture();
        fixture.Membership.Role = GroupMemberRole.Member;

        var act = () => fixture.PostAsync();

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
        fixture.Outbox.VerifyNoOtherCalls();
        fixture.Client.VerifyNoOtherCalls();
        fixture.UnitOfWork.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PickupPalAnnouncementSendResult.Sent, OutboxHandlingDisposition.Completed)]
    [InlineData(PickupPalAnnouncementSendResult.Retry, OutboxHandlingDisposition.Retry)]
    [InlineData(PickupPalAnnouncementSendResult.Rejected, OutboxHandlingDisposition.Fail)]
    public async Task HandleAsync_WhenRetryingExistingAnnouncement_SendsSavedBodyWithoutInsertingLocalPost(
        PickupPalAnnouncementSendResult outcome, OutboxHandlingDisposition disposition)
    {
        var fixture = new Fixture();
        var announcement = new Announcement { GroupChatId = fixture.Group.Id, Body = "Saved body" };
        fixture.Announcements.Setup(x => x.GetByIdAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);
        fixture.Client.Setup(x => x.SendAsync(fixture.Group.ExternalId, announcement.Body, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var handler = new AnnouncementPickupPalOutboxHandler(fixture.Announcements.Object, fixture.Groups.Object, fixture.Client.Object);

        var result = await handler.HandleAsync(new OutboxMessage { PayloadJson = JsonSerializer.Serialize(new { AnnouncementId = announcement.Id }) });

        result.Disposition.Should().Be(disposition);
        fixture.Client.VerifyAll();
        fixture.Announcements.Verify(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"AnnouncementId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task HandleAsync_WhenRetryPayloadInvalid_FailsWithoutSending(string payload)
    {
        var fixture = new Fixture();
        var handler = new AnnouncementPickupPalOutboxHandler(fixture.Announcements.Object, fixture.Groups.Object, fixture.Client.Object);

        var result = await handler.HandleAsync(new OutboxMessage { PayloadJson = payload });

        result.Should().Be(OutboxHandlingResult.Fail("InvalidPayload"));
        fixture.Client.VerifyNoOtherCalls();
        fixture.Announcements.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false, "AnnouncementNotFound")]
    [InlineData(true, false, "GroupDestinationNotFound")]
    [InlineData(true, true, "GroupDestinationNotFound")]
    public async Task HandleAsync_WhenRetryDestinationMissing_FailsWithoutSending(bool hasAnnouncement, bool hasGroup, string code)
    {
        var fixture = new Fixture();
        var announcement = new Announcement { GroupChatId = Guid.NewGuid(), Body = "Saved body" };
        fixture.Announcements.Setup(x => x.GetByIdAsync(announcement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAnnouncement ? announcement : null);
        fixture.Groups.Setup(x => x.GetByIdAsync(announcement.GroupChatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasGroup ? new GroupChat { ExternalId = " " } : null);
        var handler = new AnnouncementPickupPalOutboxHandler(fixture.Announcements.Object, fixture.Groups.Object, fixture.Client.Object);

        var result = await handler.HandleAsync(new OutboxMessage { PayloadJson = JsonSerializer.Serialize(new { AnnouncementId = announcement.Id }) });

        result.Should().Be(OutboxHandlingResult.Fail(code));
        fixture.Client.VerifyNoOtherCalls();
    }

    private sealed class Fixture
    {
        public GroupChat Group { get; } = new() { GroupName = "Saturday", ExternalId = "123@g.us" };
        public PlayerGroupLink Membership { get; } = new() { Status = GroupMembershipStatus.Approved, Role = GroupMemberRole.Admin };
        public Mock<IAnnouncementRepository> Announcements { get; } = new();
        public Mock<IGroupChatRepository> Groups { get; } = new();
        public Mock<IOutboxMessageRepository> Outbox { get; } = new();
        public Mock<IPickupPalAnnouncementClient> Client { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Announcement? AddedAnnouncement { get; private set; }
        public OutboxMessage? AddedDelivery { get; private set; }
        private readonly PostAnnouncementCommandHandler handler;

        public Fixture()
        {
            var userId = Guid.NewGuid();
            var profile = new PlayerProfile { IdentityUserId = userId };
            var user = new Mock<ICurrentUser>();
            user.SetupGet(x => x.UserId).Returns(userId);
            var profiles = new Mock<IPlayerProfileRepository>();
            profiles.Setup(x => x.FindByIdentityUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
            var links = new Mock<IPlayerGroupLinkRepository>();
            links.Setup(x => x.FindLinkAsync(profile.Id, Group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Membership);
            Groups.Setup(x => x.GetByIdAsync(Group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Group);
            Announcements.Setup(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()))
                .Callback<Announcement, CancellationToken>((value, _) => AddedAnnouncement = value).Returns(Task.CompletedTask);
            Outbox.Setup(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .Callback<OutboxMessage, CancellationToken>((value, _) => AddedDelivery = value).Returns(Task.CompletedTask);
            var clock = new Mock<IClock>();
            clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
            handler = new PostAnnouncementCommandHandler(new PostAnnouncementCommandValidator(), user.Object,
                profiles.Object, links.Object, Groups.Object, Announcements.Object, UnitOfWork.Object,
                clock.Object, Outbox.Object, Client.Object);
        }

        public Task<SentAnnouncementSummary> PostAsync() => handler.HandleAsync(new PostAnnouncementCommand(Group.Id, "  Pitch changed  ", false));
    }
}
