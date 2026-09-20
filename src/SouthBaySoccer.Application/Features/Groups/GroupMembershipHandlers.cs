using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Shared lookups for the membership handlers.</summary>
internal static class GroupMembershipAccess
{
    public static async Task<PlayerProfile> RequireProfileAsync(
        ICurrentUser currentUser,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        return await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
    }

    public static async Task<GroupChat> RequireGroupAsync(
        IGroupChatRepository groupChatRepository,
        Guid groupChatId,
        CancellationToken cancellationToken) =>
        await groupChatRepository.GetByIdAsync(groupChatId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Group chat was not found.");

    /// <summary>A super admin, or an approved admin of this specific group.</summary>
    public static async Task<bool> CanManageGroupAsync(
        ICurrentUser currentUser,
        IPlayerGroupLinkRepository playerGroupLinkRepository,
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        if (GroupMembershipAuthorization.IsSuperAdmin(currentUser))
        {
            return true;
        }

        var row = await playerGroupLinkRepository.FindLinkAsync(playerProfileId, groupChatId, cancellationToken);
        return row is { Status: GroupMembershipStatus.Approved, Role: GroupMemberRole.Admin };
    }

    public static async Task EnsureCanManageGroupAsync(
        ICurrentUser currentUser,
        IPlayerGroupLinkRepository playerGroupLinkRepository,
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        if (!await CanManageGroupAsync(currentUser, playerGroupLinkRepository, playerProfileId, groupChatId, cancellationToken))
        {
            throw new ApplicationForbiddenException("Only a group admin or a super admin can manage this group's members.");
        }
    }

    public static void EnsureSuperAdmin(ICurrentUser currentUser)
    {
        if (!GroupMembershipAuthorization.IsSuperAdmin(currentUser))
        {
            throw new ApplicationForbiddenException("Only a super admin can do this.");
        }
    }

    public static async Task<MyGroupMembershipsModel> BuildMyMembershipsAsync(
        ICurrentUser currentUser,
        IPlayerGroupLinkRepository playerGroupLinkRepository,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var rows = await playerGroupLinkRepository.ListPlayerMembershipsAsync(playerProfileId, cancellationToken);
        return new MyGroupMembershipsModel(
            GroupMembershipAuthorization.IsSuperAdmin(currentUser),
            rows.Any(row => row.Status == GroupMembershipStatus.Approved),
            rows.Select(row => new GroupMembershipModel(
                    row.GroupChatId,
                    row.GroupName,
                    row.Status,
                    row.Role,
                    row.Source,
                    row.RequestedAtUtc,
                    row.ApprovedAtUtc))
                .ToArray());
    }

    public static async Task<GroupMembersModel> BuildGroupMembersAsync(
        ICurrentUser currentUser,
        IPlayerGroupLinkRepository playerGroupLinkRepository,
        GroupChat group,
        CancellationToken cancellationToken)
    {
        var rows = await playerGroupLinkRepository.ListGroupMembersAsync(group.Id, cancellationToken);
        var pending = rows.Where(row => row.Status == GroupMembershipStatus.Pending).Select(ToModel).ToArray();
        // Admins first so the people who run the group are visible at the top of the list.
        var members = rows
            .Where(row => row.Status == GroupMembershipStatus.Approved)
            .OrderByDescending(row => row.Role == GroupMemberRole.Admin)
            .Select(ToModel)
            .ToArray();
        return new GroupMembersModel(
            group.Id,
            group.GroupName,
            CanManageMembers: true,
            CanAppointAdmins: GroupMembershipAuthorization.IsSuperAdmin(currentUser),
            pending,
            members);
    }

    private static GroupMemberModel ToModel(GroupMemberReadModel row) =>
        new(
            row.PlayerProfileId,
            row.DisplayName,
            PlayerInitials.Build(row.DisplayName),
            row.Status,
            row.Role,
            row.Source,
            row.RequestedAtUtc,
            row.ApprovedAtUtc);
}

/// <summary>Every group with the caller's status and role; pending counts only where the caller can manage.</summary>
public sealed class GetGroupCatalogQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IPickupPalGroupClient groupClient,
    IUnitOfWork unitOfWork)
{
    public async Task<GroupCatalogModel> HandleAsync(GetGroupCatalogQuery query, CancellationToken cancellationToken = default)
    {
        _ = query;
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        var groups = await groupChatRepository.ListAllAsync(cancellationToken);
        var counts = await playerGroupLinkRepository.CountByGroupAsync(groups.Select(group => group.Id).ToArray(), cancellationToken);
        var mine = (await playerGroupLinkRepository.ListByPlayerAsync(profile.Id, cancellationToken))
            .ToDictionary(row => row.GroupChatId);
        var isSuperAdmin = GroupMembershipAuthorization.IsSuperAdmin(currentUser);

        return new GroupCatalogModel(groups.Select(group =>
        {
            var row = mine.GetValueOrDefault(group.Id);
            var canManage = isSuperAdmin || row is { Status: GroupMembershipStatus.Approved, Role: GroupMemberRole.Admin };
            var groupCounts = counts.GetValueOrDefault(group.Id) ?? new GroupMembershipCounts(0, 0);
            return new GroupWithMembershipModel(
                group.Id,
                group.GroupName,
                groupCounts.ApprovedCount,
                row?.Status,
                row is { Status: GroupMembershipStatus.Approved } ? row.Role : GroupMemberRole.Member,
                canManage ? groupCounts.PendingCount : 0);
        }).ToArray());
    }

    private async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PickupPalGroupChat> externalGroups;
        try
        {
            externalGroups = await groupClient.GetAllGroupsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Provider availability must not hide the persisted catalogue or memberships.
            return;
        }

        var changed = false;
        foreach (var source in externalGroups
                     .Where(group => !string.IsNullOrWhiteSpace(group.ExternalId))
                     .DistinctBy(group => group.ExternalId, StringComparer.Ordinal))
        {
            var result = await GroupUpsert.UpsertAsync(groupChatRepository, source, cancellationToken);
            changed |= result.Changed;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class GetMyGroupMembershipsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository)
{
    public async Task<MyGroupMembershipsModel> HandleAsync(GetMyGroupMembershipsQuery query, CancellationToken cancellationToken = default)
    {
        _ = query;
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        return await GroupMembershipAccess.BuildMyMembershipsAsync(currentUser, playerGroupLinkRepository, profile.Id, cancellationToken);
    }
}

public sealed class RequestGroupMembershipsCommandHandler(
    IValidator<RequestGroupMembershipsCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService)
{
    public async Task<MyGroupMembershipsModel> HandleAsync(RequestGroupMembershipsCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var requestedIds = command.GroupChatIds.Distinct().ToArray();
        var groups = await groupChatRepository.ListByIdsAsync(requestedIds, cancellationToken);
        if (groups.Count != requestedIds.Length)
        {
            throw new ApplicationNotFoundException("Group chat was not found.");
        }

        await membershipService.RequestAsync(profile, groups, cancellationToken);
        return await GroupMembershipAccess.BuildMyMembershipsAsync(currentUser, playerGroupLinkRepository, profile.Id, cancellationToken);
    }
}

/// <summary>
/// The player leaves a group (Approved -> Removed by themselves) or withdraws their own pending
/// request (Pending -> Withdrawn). Rows that already ended are left alone, so repeating the call
/// simply returns the current memberships.
/// </summary>
public sealed class LeaveGroupCommandHandler(
    IValidator<LeaveGroupCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService)
{
    public async Task<MyGroupMembershipsModel> HandleAsync(LeaveGroupCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var row = await playerGroupLinkRepository.FindMembershipAsync(profile.Id, command.GroupChatId, cancellationToken)
            ?? throw new ApplicationNotFoundException("You are not a member of that group.");
        switch (row.Status)
        {
            case GroupMembershipStatus.Pending:
                await membershipService.WithdrawAsync(row, cancellationToken);
                break;
            case GroupMembershipStatus.Approved:
                await membershipService.RemoveAsync(row, profile.Id, cancellationToken);
                break;
            default:
                // Declined / Removed / Withdrawn: nothing left to leave.
                break;
        }

        return await GroupMembershipAccess.BuildMyMembershipsAsync(currentUser, playerGroupLinkRepository, profile.Id, cancellationToken);
    }
}

public sealed class GetGroupMembersQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository)
{
    public async Task<GroupMembersModel> HandleAsync(GetGroupMembersQuery query, CancellationToken cancellationToken = default)
    {
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var group = await GroupMembershipAccess.RequireGroupAsync(groupChatRepository, query.GroupChatId, cancellationToken);
        await GroupMembershipAccess.EnsureCanManageGroupAsync(currentUser, playerGroupLinkRepository, profile.Id, group.Id, cancellationToken);
        return await GroupMembershipAccess.BuildGroupMembersAsync(currentUser, playerGroupLinkRepository, group, cancellationToken);
    }
}

/// <summary>Approve, decline, or remove a member of a group the caller administers (or any group for a super admin).</summary>
public sealed class ReviewGroupMemberCommandHandler(
    IValidator<ReviewGroupMemberCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService)
{
    public async Task<GroupMembersModel> HandleAsync(ReviewGroupMemberCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var group = await GroupMembershipAccess.RequireGroupAsync(groupChatRepository, command.GroupChatId, cancellationToken);
        await GroupMembershipAccess.EnsureCanManageGroupAsync(currentUser, playerGroupLinkRepository, actor.Id, group.Id, cancellationToken);
        var row = await playerGroupLinkRepository.FindMembershipAsync(command.PlayerProfileId, group.Id, cancellationToken)
            ?? throw new ApplicationNotFoundException("That player has no membership in this group.");

        switch (command.Review)
        {
            case GroupMemberReview.Approve:
                await membershipService.ApproveAsync(row, actor.Id, cancellationToken);
                break;
            case GroupMemberReview.Decline:
                await membershipService.DeclineAsync(row, actor.Id, cancellationToken);
                break;
            case GroupMemberReview.Remove:
                if (row.Role == GroupMemberRole.Admin && !GroupMembershipAuthorization.IsSuperAdmin(currentUser))
                {
                    throw new ApplicationForbiddenException("Only a super admin can remove a group admin.");
                }

                await membershipService.RemoveAsync(row, actor.Id, cancellationToken);
                break;
            default:
                throw new ApplicationConflictException("Unknown member review.");
        }

        return await GroupMembershipAccess.BuildGroupMembersAsync(currentUser, playerGroupLinkRepository, group, cancellationToken);
    }
}

public sealed class AddGroupMemberCommandHandler(
    IValidator<AddGroupMemberCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService)
{
    public async Task<GroupMembersModel> HandleAsync(AddGroupMemberCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        GroupMembershipAccess.EnsureSuperAdmin(currentUser);
        var actor = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var group = await GroupMembershipAccess.RequireGroupAsync(groupChatRepository, command.GroupChatId, cancellationToken);
        _ = await playerProfileRepository.FindProfileAsync(command.PlayerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        await membershipService.AddDirectlyAsync(command.PlayerProfileId, group.Id, actor.Id, cancellationToken);
        return await GroupMembershipAccess.BuildGroupMembersAsync(currentUser, playerGroupLinkRepository, group, cancellationToken);
    }
}

public sealed class SetGroupAdminCommandHandler(
    IValidator<SetGroupAdminCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService)
{
    public async Task<GroupMembersModel> HandleAsync(SetGroupAdminCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        GroupMembershipAccess.EnsureSuperAdmin(currentUser);
        _ = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        var group = await GroupMembershipAccess.RequireGroupAsync(groupChatRepository, command.GroupChatId, cancellationToken);
        var row = await playerGroupLinkRepository.FindMembershipAsync(command.PlayerProfileId, group.Id, cancellationToken)
            ?? throw new ApplicationConflictException("Only an approved member can be a group admin.");

        await membershipService.SetRoleAsync(row, command.IsAdmin ? GroupMemberRole.Admin : GroupMemberRole.Member, cancellationToken);
        return await GroupMembershipAccess.BuildGroupMembersAsync(currentUser, playerGroupLinkRepository, group, cancellationToken);
    }
}

/// <summary>
/// Name-fragment search for the add-member flow. Only super admins and players who administer at
/// least one group may search; results carry the masked phone only.
/// </summary>
public sealed class SearchPlayersQueryHandler(
    IValidator<SearchPlayersQuery> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository)
{
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<PlayerSearchResultModel>> HandleAsync(SearchPlayersQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        var profile = await GroupMembershipAccess.RequireProfileAsync(currentUser, playerProfileRepository, cancellationToken);
        if (!GroupMembershipAuthorization.IsSuperAdmin(currentUser))
        {
            var administers = (await playerGroupLinkRepository.ListApprovedByPlayerAsync(profile.Id, cancellationToken))
                .Any(row => row.Role == GroupMemberRole.Admin);
            if (!administers)
            {
                throw new ApplicationForbiddenException("Only a group admin or a super admin can search players.");
            }
        }

        var results = await playerProfileRepository.SearchByDisplayNameAsync(
            query.Query.Trim().ToUpperInvariant(),
            MaxResults,
            cancellationToken);
        return results
            .Select(player => new PlayerSearchResultModel(
                player.Id,
                player.DisplayName,
                PlayerInitials.Build(player.DisplayName),
                player.MaskedPhoneNumber))
            .ToArray();
    }
}
