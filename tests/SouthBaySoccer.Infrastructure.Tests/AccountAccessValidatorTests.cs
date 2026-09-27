using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Identity;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class AccountAccessValidatorTests(InfrastructureDatabaseFixture database)
{
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, -1, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 1, true)]
    public async Task IsActiveAsync_WhenIdentityExists_UsesCurrentLockoutState(
        bool enabled, int? lockoutOffsetSeconds, bool expected)
    {
        await using var db = database.CreateDbContext();
        var user = new ApplicationIdentityUser
        {
            Id = Guid.NewGuid(), UserName = $"access-{Guid.NewGuid():N}",
            LockoutEnabled = enabled,
            LockoutEnd = lockoutOffsetSeconds is { } seconds ? new DateTimeOffset(Now.AddSeconds(seconds)) : null,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AccountAccessValidator(db, Mock.Of<IClock>(x => x.UtcNow == Now));
        db.ChangeTracker.Clear();

        var active = await validator.IsActiveAsync(user.Id);

        active.Should().Be(expected);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task IsActiveAsync_WhenIdentityDoesNotExist_ReturnsFalse()
    {
        await using var db = database.CreateDbContext();
        var validator = new AccountAccessValidator(db, Mock.Of<IClock>(x => x.UtcNow == Now));

        var active = await validator.IsActiveAsync(Guid.NewGuid());

        active.Should().BeFalse();
    }
}
