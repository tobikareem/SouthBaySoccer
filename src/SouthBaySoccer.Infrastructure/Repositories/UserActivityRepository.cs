using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class UserActivityRepository(SouthBaySoccerDbContext dbContext) : IUserActivityRepository
{
    public async Task<UserActivityPageReadModel> ReadPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var visible = from activity in dbContext.UserActivities.AsNoTracking()
                      join profile in dbContext.PlayerProfiles.AsNoTracking() on activity.PlayerProfileId equals profile.Id
                      select new { Activity = activity, profile.DisplayName };
        var started = await visible.Select(row => (DateTime?)row.Activity.OccurredAtUtc).MinAsync(cancellationToken);
        var window = await visible.OrderByDescending(row => row.Activity.OccurredAtUtc)
            .ThenByDescending(row => row.Activity.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var rows = window.Take(pageSize).ToArray();
        if (rows.Length == 0)
        {
            return new([], false, started);
        }
        var playerIds = rows.Select(row => row.Activity.PlayerProfileId).Distinct().ToArray();
        var summaries = await dbContext.UserActivities.AsNoTracking()
            .Where(activity => playerIds.Contains(activity.PlayerProfileId))
            .GroupBy(activity => activity.PlayerProfileId)
            .Select(group => new
            {
                PlayerId = group.Key,
                First = group.Min(activity => activity.OccurredAtUtc),
                Last = group.Max(activity => activity.OccurredAtUtc),
                SignIns = group.LongCount(activity => activity.ActivityType == UserActivityType.SignIn),
            }).ToDictionaryAsync(row => row.PlayerId, cancellationToken);
        var memberships = await (from link in dbContext.PlayerGroupLinks.AsNoTracking()
                                 join chat in dbContext.GroupChats.AsNoTracking() on link.GroupChatId equals chat.Id
                                 where playerIds.Contains(link.PlayerProfileId)
                                 orderby chat.GroupName, chat.Id
                                 select new { link.PlayerProfileId, chat.GroupName, link.Status }).ToArrayAsync(cancellationToken);
        var groupsByPlayer = memberships.ToLookup(row => row.PlayerProfileId);
        return new(rows.Select(row =>
        {
            var activity = row.Activity;
            var summary = summaries[activity.PlayerProfileId];
            return new UserActivityReadModel(activity.Id, activity.PlayerProfileId, row.DisplayName,
                activity.ActivityType, activity.OccurredAtUtc, summary.First, summary.Last, summary.SignIns,
                groupsByPlayer[activity.PlayerProfileId].Select(group => new UserActivityGroupReadModel(group.GroupName, group.Status.ToString())).ToArray());
        }).ToArray(), window.Length > pageSize, started);
    }
}
