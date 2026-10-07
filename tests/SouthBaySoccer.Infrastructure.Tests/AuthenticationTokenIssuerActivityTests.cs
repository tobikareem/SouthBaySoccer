using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class AuthenticationTokenIssuerActivityTests
{
    internal static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(UserActivityType.SignIn)]
    [InlineData(UserActivityType.SignUp)]
    public async Task IssueTokensAsync_WhenSuccessful_AddsOneActivityWithInitialTokenInSameSave(UserActivityType kind)
    {
        await using var db = new CapturingContext();
        var subject = new AuthenticationTokenSubject(Guid.NewGuid(), Guid.NewGuid(), ["Player"]) { ActivityType = kind };

        await CreateIssuer(db).IssueTokensAsync(subject);
        await db.SaveChangesAsync(); // Retrying SaveChanges must not add another event.

        db.Events.Should().ContainSingle();
        var activity = db.Events.Single();
        var token = db.Tokens.Should().ContainSingle().Subject;
        activity.ActivityType.Should().Be(kind);
        activity.IdentityUserId.Should().Be(subject.IdentityUserId);
        activity.PlayerProfileId.Should().Be(subject.PlayerProfileId);
        activity.SessionFamilyId.Should().Be(token.FamilyId);
        activity.OccurredAtUtc.Should().Be(Now);
        db.InitialSaveContainedBoth.Should().BeTrue();
    }

    [Fact]
    public async Task IssueTokensAsync_WhenWriteFails_DetachesBothRowsBeforeSubsequentSaves()
    {
        await using var db = new CapturingContext { FailSave = true };
        var subject = new AuthenticationTokenSubject(Guid.NewGuid(), Guid.NewGuid(), ["Player"]);

        await ((Func<Task>)(() => CreateIssuer(db).IssueTokensAsync(subject))).Should().ThrowAsync<DbUpdateException>();

        db.ChangeTracker.Entries<UserActivity>().Should().BeEmpty();
        db.ChangeTracker.Entries<RefreshToken>().Should().BeEmpty();
        db.FailSave = false;
        await db.SaveChangesAsync();
        db.Events.Should().BeEmpty();
        db.Tokens.Should().BeEmpty();
    }

    [Fact]
    public void AuthenticationTokenSubject_WhenNoKindSpecified_DefaultsToSignIn() =>
        new AuthenticationTokenSubject(Guid.NewGuid(), Guid.NewGuid(), []).ActivityType.Should().Be(UserActivityType.SignIn);

    internal static AuthenticationTokenIssuer CreateIssuer(SouthBaySoccerDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var tokens = new Mock<ITokenService>();
        tokens.Setup(x => x.IssueAccessToken(It.IsAny<AccessTokenIssueRequest>())).Returns(new IssuedAccessToken("access", Now.AddMinutes(15), "test"));
        var secret = new Mock<IRefreshTokenSecretGenerator>();
        secret.Setup(x => x.CreateToken()).Returns(() => Guid.NewGuid().ToString("N"));
        return new AuthenticationTokenIssuer(db, clock.Object, tokens.Object, new RefreshTokenHasher(), secret.Object);
    }

    private sealed class CapturingContext() : SouthBaySoccerDbContext(new DbContextOptionsBuilder<SouthBaySoccerDbContext>()
        .UseSqlServer("Server=localhost;Database=DesignOnly;User Id=unused;Password=unused;TrustServerCertificate=True").Options)
    {
        public bool FailSave { get; set; }
        public bool InitialSaveContainedBoth { get; private set; }
        public List<UserActivity> Events { get; } = [];
        public List<RefreshToken> Tokens { get; } = [];

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailSave) throw new DbUpdateException("Write failed.");
            var events = ChangeTracker.Entries<UserActivity>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
            var tokens = ChangeTracker.Entries<RefreshToken>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
            InitialSaveContainedBoth |= events.Length == 1 && tokens.Length == 1;
            Events.AddRange(events);
            Tokens.AddRange(tokens);
            ChangeTracker.AcceptAllChanges();
            return Task.FromResult(events.Length + tokens.Length);
        }
    }
}
