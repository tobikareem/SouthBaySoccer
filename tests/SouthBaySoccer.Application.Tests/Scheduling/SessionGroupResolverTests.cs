using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class SessionGroupResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenGroupIsRequested_ReturnsThatGroup()
    {
        var context = new TestContext(linkCount: 2);

        var group = await context.Resolver.ResolveAsync(context.Groups[1].Id);

        group.Should().BeSameAs(context.Groups[1]);
    }

    [Fact]
    public async Task ResolveAsync_WhenRequestedGroupDoesNotExist_Throws()
    {
        var context = new TestContext(linkCount: 1);

        var act = () => context.Resolver.ResolveAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
    }

    [Fact]
    public async Task ResolveAsync_WhenAdminHasExactlyOneLink_DefaultsToThatGroup()
    {
        var context = new TestContext(linkCount: 1);

        var group = await context.Resolver.ResolveAsync(null);

        group.Should().BeSameAs(context.Groups[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ResolveAsync_WhenAdminHasZeroOrSeveralLinks_LeavesTheSessionAppOnly(int linkCount)
    {
        var context = new TestContext(linkCount);

        var group = await context.Resolver.ResolveAsync(null);

        group.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WhenNobodyIsSignedIn_LeavesTheSessionAppOnly()
    {
        var context = new TestContext(linkCount: 1, signedIn: false);

        var group = await context.Resolver.ResolveAsync(null);

        group.Should().BeNull();
    }

    private sealed class TestContext
    {
        public List<GroupChat> Groups { get; } = [];

        public SessionGroupResolver Resolver { get; }

        public TestContext(int linkCount, bool signedIn = true)
        {
            var identityUserId = Guid.NewGuid();
            var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Admin" };
            var groupRepository = new Mock<IGroupChatRepository>();
            var links = new List<PlayerGroupLink>();
            for (var i = 0; i < Math.Max(linkCount, 2); i++)
            {
                var group = new GroupChat { Id = Guid.NewGuid(), ExternalId = $"group-{i}@g.us", GroupName = $"Group {i}" };
                Groups.Add(group);
                groupRepository.Setup(x => x.GetByIdAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
                if (i < linkCount)
                {
                    links.Add(new PlayerGroupLink { Id = Guid.NewGuid(), PlayerProfileId = profile.Id, GroupChatId = group.Id });
                }
            }

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(signedIn ? identityUserId : null);
            var profileRepository = new Mock<IPlayerProfileRepository>();
            profileRepository.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
            var linkRepository = new Mock<IPlayerGroupLinkRepository>();
            linkRepository.Setup(x => x.ListApprovedByPlayerAsync(profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(links);

            Resolver = new SessionGroupResolver(currentUser.Object, profileRepository.Object, linkRepository.Object, groupRepository.Object);
        }
    }
}
