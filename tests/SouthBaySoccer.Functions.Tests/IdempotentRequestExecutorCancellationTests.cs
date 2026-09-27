using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Domain.Entities.Announcements;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class IdempotentRequestExecutorCancellationTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCancelledDuringPickupPalSend_CompletesSavedPostAndReplaysWithoutPostingAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        var group = new GroupChat { GroupName = "Saturday", ExternalId = "123@g.us" };
        var profile = new PlayerProfile { IdentityUserId = fixture.User.UserId };
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(fixture.User.UserId.GetValueOrDefault(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var links = new Mock<IPlayerGroupLinkRepository>();
        links.Setup(x => x.FindLinkAsync(profile.Id, group.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerGroupLink { Status = GroupMembershipStatus.Approved, Role = GroupMemberRole.Admin });
        var groups = new Mock<IGroupChatRepository>();
        groups.Setup(x => x.GetByIdAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
        var announcements = new Mock<IAnnouncementRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var committed = false;
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => committed = true).ReturnsAsync(2);
        var client = new Mock<IPickupPalAnnouncementClient>();
        client.Setup(x => x.SendAsync(group.ExternalId, "Pitch changed", It.IsAny<CancellationToken>()))
            .Returns((string _, string _, CancellationToken token) =>
            {
                committed.Should().BeTrue();
                token.Should().Be(cancellation.Token);
                cancellation.Cancel();
                return Task.FromCanceled<PickupPalAnnouncementSendResult>(token);
            });
        var handler = new PostAnnouncementCommandHandler(new PostAnnouncementCommandValidator(), fixture.User,
            profiles.Object, links.Object, groups.Object, announcements.Object, unitOfWork.Object,
            fixture.Clock, Mock.Of<IOutboxMessageRepository>(), client.Object);
        var command = new PostAnnouncementCommand(group.Id, "Pitch changed", false);
        async Task<IdempotentResponse<SentAnnouncementSummary>> PostAsync(CancellationToken token) =>
            new(HttpStatusCode.Created, await handler.HandleAsync(command, token));

        var act = () => fixture.Executor.ExecuteAsync(fixture.Request(), "PostAnnouncement", "same-key",
            command, PostAsync, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        var replay = await fixture.Executor.ExecuteAsync(fixture.Request(), "PostAnnouncement", "same-key",
            command, PostAsync);

        fixture.Record.Should().NotBeNull();
        var record = fixture.Record ?? throw new InvalidOperationException("No completed request");
        record.CompletedAtUtc.Should().NotBeNull();
        fixture.CompletionToken.CanBeCanceled.Should().BeTrue();
        fixture.CompletionToken.Should().NotBe(cancellation.Token);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Body.Position = 0;
        var replayJson = await new StreamReader(replay.Body).ReadToEndAsync();
        replayJson.Should().Be(record.ResponseBodyJson);
        using var replayDocument = JsonDocument.Parse(replayJson);
        replayDocument.RootElement.GetProperty("body").GetString().Should().Be(command.Body);
        announcements.Verify(x => x.AddAsync(It.IsAny<Announcement>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Store.Verify(x => x.AbandonAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationCancelsBeforeCommit_AbandonsWithoutCompleting()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();

        var act = () => fixture.Executor.ExecuteAsync<object>(fixture.Request(), "PostAnnouncement", "same-key",
            new { Body = "Pitch changed" }, token =>
            {
                token.Should().Be(cancellation.Token);
                cancellation.Cancel();
                return Task.FromCanceled<IdempotentResponse<object>>(token);
            }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Verify(x => x.AbandonAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), CancellationToken.None), Times.Once);
        fixture.Store.Verify(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public ICurrentUser User { get; } = Mock.Of<ICurrentUser>(x => x.UserId == Guid.NewGuid());
        public IClock Clock { get; } = Mock.Of<IClock>(x => x.UtcNow == new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc));
        public Mock<IIdempotencyStore> Store { get; } = new();
        public IdempotencyRecordModel? Record { get; private set; }
        public CancellationToken CompletionToken { get; private set; }
        public IdempotentRequestExecutor Executor { get; }

        public Fixture()
        {
            Store.Setup(x => x.FindAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Record);
            Store.Setup(x => x.CreateAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid? _, Guid? _, string _, string _, string hash, DateTime _, CancellationToken _) =>
                    Record = new IdempotencyRecordModel(Guid.NewGuid(), hash, null, null, null));
            Store.Setup(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, int status, string json, string _, DateTime completedAt, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    CompletionToken = token;
                    Record = (Record ?? throw new InvalidOperationException("No reserved request")) with
                    { ResponseStatusCode = status, ResponseBodyJson = json, CompletedAtUtc = completedAt };
                }).Returns(Task.CompletedTask);
            Executor = new IdempotentRequestExecutor(User, Clock, Store.Object);
        }

        public HttpRequestData Request()
        {
            var context = Mock.Of<FunctionContext>();
            var response = new Mock<HttpResponseData>(context);
            response.SetupProperty(x => x.StatusCode);
            response.SetupProperty(x => x.Headers, new HttpHeadersCollection());
            response.SetupProperty(x => x.Body, new MemoryStream());
            var request = new Mock<HttpRequestData>(context);
            request.Setup(x => x.CreateResponse()).Returns(response.Object);
            return request.Object;
        }
    }
}
