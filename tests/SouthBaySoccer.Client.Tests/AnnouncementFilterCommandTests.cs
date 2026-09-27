using FluentAssertions;
using Moq;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using Xunit;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// SegmentedControl invokes its selection command with whatever <c>SelectedItem</c> currently holds,
/// and that is null while the segments are being rebuilt — FilterLabels is re-raised on every
/// UnreadCount change. A typed RelayCommand&lt;T&gt; throws on a null or wrong-typed argument, and
/// the throw escapes as a crash rather than a binding warning, so the command has to absorb it.
/// See .ai/lessons/2026-06-25-maui-xaml-commandparameter-types.md.
/// </summary>
public sealed class AnnouncementFilterCommandTests
{
    private static AnnouncementsPageModel CreateModel()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 17, 0, 0, TimeSpan.Zero));
        return new AnnouncementsPageModel(
            Mock.Of<IAnnouncementsClient>(),
            Mock.Of<IAnnouncementsNavigator>(),
            new ClientResponseCache(time),
            time);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void ApplyFilter_WhenLabelIsNull_DoesNotThrow()
    {
        var model = CreateModel();

        var act = () => model.ApplyFilterCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ApplyFilter_WhenLabelIsNull_LeavesTheCurrentFilterUntouched()
    {
        var model = CreateModel();
        model.ApplyFilterCommand.Execute("Unread · 3");

        model.ApplyFilterCommand.Execute(null);

        model.ShowUnreadOnly.Should().BeTrue("a missing label means no selection, not a reset");
    }

    [Fact]
    public void ApplyFilter_WhenLabelIsAll_ShowsEverything()
    {
        var model = CreateModel();
        model.ApplyFilterCommand.Execute("Unread · 3");

        model.ApplyFilterCommand.Execute("All");

        model.ShowUnreadOnly.Should().BeFalse();
    }

    [Fact]
    public void ApplyFilter_WhenLabelIsUnread_ShowsUnreadOnly()
    {
        var model = CreateModel();

        model.ApplyFilterCommand.Execute("Unread · 3");

        model.ShowUnreadOnly.Should().BeTrue();
    }
}
