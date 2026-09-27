using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Returns every available Pickup Pal group chat for the sign-in selection list.</summary>
public sealed class GetAvailableGroupsQueryHandler(
    IPickupPalGroupClient groupClient,
    GroupNameVisibility groupNameVisibility)
{
    public async Task<IReadOnlyList<GroupSummary>> HandleAsync(
        GetAvailableGroupsQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        // This is the raw catalog for the sign-in picker: Id/IsLinked/IsPrimary are not populated
        // here (there is no player context), so callers that need per-player link state must read
        // GetMyGroups. Kept intentionally player-agnostic so it can be cached/reused across players.
        var groups = await groupClient.GetAllGroupsAsync(cancellationToken);
        return groups
            .Where(group => !string.IsNullOrWhiteSpace(group.ExternalId))
            .Where(group => groupNameVisibility.IsVisible(
                string.IsNullOrWhiteSpace(group.GroupName) ? group.ExternalId : group.GroupName))
            .Select(group => new GroupSummary(
                Guid.Empty,
                group.ExternalId,
                string.IsNullOrWhiteSpace(group.GroupName) ? group.ExternalId : group.GroupName,
                group.WhatsAppMemberCount,
                IsLinked: false,
                IsPrimary: false))
            .ToArray();
    }
}

/// <summary>
/// Returns the current player's approved groups (the legacy sign-in shape). Our database is the
/// source of truth; the external Pickup Pal <c>/linked</c> read only seeds approved WhatsApp
/// memberships for pairs that have no row yet, so a read failure there never blocks a player who
/// already has memberships in our database. <c>IsLinked</c> means "has at least one approved
/// membership".
/// </summary>
public sealed class GetMyGroupsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGroupClient groupClient,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService,
    IUnitOfWork unitOfWork,
    GroupNameVisibility groupNameVisibility)
{
    public async Task<MyGroupsResult> HandleAsync(
        GetMyGroupsQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        // A profile without a Pickup Pal user id (for example a guest) cannot be reconciled against
        // WhatsApp membership. Report it as linked so the sign-in gate does not trap it.
        if (string.IsNullOrWhiteSpace(profile.PickupPalUserId))
        {
            return new MyGroupsResult(IsLinked: true, Groups: []);
        }

        await SeedLinksFromPickupPalAsync(profile, profile.PickupPalUserId, cancellationToken);

        var links = (await playerGroupLinkRepository.ListPlayerGroupsAsync(profile.Id, cancellationToken))
            .Where(link => groupNameVisibility.IsVisible(link.GroupName)).ToArray();
        return new MyGroupsResult(
            IsLinked: links.Length > 0,
            Groups: links.Select(ToSummary).ToArray());
    }

    // Mirrors any WhatsApp-side memberships into our database as approved rows, without ever
    // writing back to Pickup Pal. Rows that already exist in any status are left alone.
    private async Task SeedLinksFromPickupPalAsync(
        PlayerProfile profile,
        string pickupPalUserId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PickupPalGroupChat> externalGroups;
        try
        {
            externalGroups = await groupClient.GetLinkedGroupsAsync(pickupPalUserId, cancellationToken);
        }
        catch (Exception)
        {
            // The external read is a best-effort cross-check; our database drives linkage, so a
            // transient Pickup Pal failure must not break sign-in. Fall back to the stored links.
            return;
        }

        if (externalGroups.Count == 0)
        {
            return;
        }

        var groups = new List<GroupChat>(externalGroups.Count);
        var groupsChanged = false;
        foreach (var externalGroup in externalGroups.Where(g => !string.IsNullOrWhiteSpace(g.ExternalId)))
        {
            var (group, groupChanged) = await GroupUpsert.UpsertAsync(groupChatRepository, externalGroup, cancellationToken);
            groupsChanged |= groupChanged;
            groups.Add(group);
        }

        if (groupsChanged)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ApplicationConflictException)
            {
                // A concurrent first-time read inserted the same group first and tripped its
                // unique index. The stored catalogue is consistent either way; seed next time.
                return;
            }
        }

        await membershipService.SeedFromWhatsAppAsync(profile, groups, cancellationToken);
    }

    private static GroupSummary ToSummary(PlayerGroupReadModel link) =>
        new(link.GroupChatId, link.ExternalId, link.GroupName, link.MemberCount, IsLinked: true, link.IsPrimary);
}

/// <summary>
/// Legacy link endpoint: asks for membership in one group through the same approval flow as the
/// multi-select request. Pickup Pal is never written; a group the player is already in on
/// WhatsApp is approved at once, otherwise the request waits for a group admin.
/// </summary>
public sealed class LinkPlayerToGroupCommandHandler(
    IValidator<LinkPlayerToGroupCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGroupClient groupClient,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    GroupMembershipService membershipService,
    IUnitOfWork unitOfWork,
    GroupNameVisibility groupNameVisibility)
{
    public async Task<MyGroupsResult> HandleAsync(
        LinkPlayerToGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        var externalId = command.GroupExternalId.Trim();
        var group = await ResolveGroupAsync(externalId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Group chat was not found.");

        await membershipService.RequestAsync(profile, [group], cancellationToken);

        var links = (await playerGroupLinkRepository.ListPlayerGroupsAsync(profile.Id, cancellationToken))
            .Where(link => groupNameVisibility.IsVisible(link.GroupName)).ToArray();
        return new MyGroupsResult(
            IsLinked: links.Length > 0,
            Groups: links.Select(link => new GroupSummary(
                link.GroupChatId, link.ExternalId, link.GroupName, link.MemberCount, IsLinked: true, link.IsPrimary)).ToArray());
    }

    // Prefer the copy already in our database; only reach out to Pickup Pal to discover a group we
    // have not persisted yet. Either way linkage stays local.
    private async Task<GroupChat?> ResolveGroupAsync(string externalId, CancellationToken cancellationToken)
    {
        var existing = await groupChatRepository.FindByExternalIdAsync(externalId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var available = await groupClient.GetAllGroupsAsync(cancellationToken);
        var match = available.FirstOrDefault(group =>
            string.Equals(group.ExternalId, externalId, StringComparison.Ordinal));
        if (match is null)
        {
            return null;
        }

        var upserted = await GroupUpsert.UpsertAsync(groupChatRepository, match, cancellationToken);
        // The new group row must exist before a membership row can reference it.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return upserted.Group;
    }
}

/// <summary>Result of a group-chat upsert: the tracked entity and whether anything actually changed.</summary>
internal readonly record struct GroupChatUpsertResult(GroupChat Group, bool Changed);

/// <summary>Shared upsert of a Pickup Pal group chat into our database, keyed on the external id.</summary>
internal static class GroupUpsert
{
    public static async Task<GroupChatUpsertResult> UpsertAsync(
        IGroupChatRepository groupChatRepository,
        PickupPalGroupChat source,
        CancellationToken cancellationToken)
    {
        var group = await groupChatRepository.FindByExternalIdAsync(source.ExternalId, cancellationToken);
        if (group is null)
        {
            group = new GroupChat { ExternalId = source.ExternalId };
            ApplyIfChanged(group, source);
            await groupChatRepository.AddAsync(group, cancellationToken);
            return new GroupChatUpsertResult(group, Changed: true);
        }

        // Only mark the entity dirty when a field actually changed, so refreshed metadata (renames,
        // member counts) is persisted while an unchanged read is not written back needlessly.
        var changed = ApplyIfChanged(group, source);
        if (changed)
        {
            groupChatRepository.Update(group);
        }

        return new GroupChatUpsertResult(group, changed);
    }

    private static bool ApplyIfChanged(GroupChat group, PickupPalGroupChat source)
    {
        var groupName = string.IsNullOrWhiteSpace(source.GroupName) ? source.ExternalId : source.GroupName;
        var changed =
            group.GroupName != groupName
            || group.LinkageCode != source.LinkageCode
            || group.Status != source.Status
            || group.WhatsAppMemberCount != source.WhatsAppMemberCount
            || group.Timezone != source.Timezone;
        if (!changed)
        {
            return false;
        }

        group.GroupName = groupName;
        group.LinkageCode = source.LinkageCode;
        group.Status = source.Status;
        group.WhatsAppMemberCount = source.WhatsAppMemberCount;
        group.Timezone = source.Timezone;
        return true;
    }
}
