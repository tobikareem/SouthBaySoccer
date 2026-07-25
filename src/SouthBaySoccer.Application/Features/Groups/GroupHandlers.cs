using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>Returns every available Pickup Pal group chat for the sign-in selection list.</summary>
public sealed class GetAvailableGroupsQueryHandler(IPickupPalGroupClient groupClient)
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
/// Returns the current player's linked groups. Our database is the source of truth; the external
/// Pickup Pal <c>/linked</c> read only seeds and cross-checks it, so a read failure there never
/// blocks a player who already has links in our database.
/// </summary>
public sealed class GetMyGroupsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGroupClient groupClient,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IUnitOfWork unitOfWork)
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

        await SeedLinksFromPickupPalAsync(profile.Id, profile.PickupPalUserId, cancellationToken);

        var links = await playerGroupLinkRepository.ListPlayerGroupsAsync(profile.Id, cancellationToken);
        return new MyGroupsResult(
            IsLinked: links.Count > 0,
            Groups: links.Select(ToSummary).ToArray());
    }

    // Mirrors any WhatsApp-side memberships into our database without ever writing back to Pickup Pal.
    private async Task SeedLinksFromPickupPalAsync(
        Guid playerProfileId,
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

        var existingLinks = await playerGroupLinkRepository.ListByPlayerAsync(playerProfileId, cancellationToken);
        var linkedGroupIds = existingLinks.Select(link => link.GroupChatId).ToHashSet();
        var hasPrimary = existingLinks.Any(link => link.IsPrimary);
        var changed = false;

        foreach (var externalGroup in externalGroups.Where(g => !string.IsNullOrWhiteSpace(g.ExternalId)))
        {
            var (group, groupChanged) = await GroupUpsert.UpsertAsync(groupChatRepository, externalGroup, cancellationToken);
            changed |= groupChanged;
            if (linkedGroupIds.Contains(group.Id))
            {
                continue;
            }

            await playerGroupLinkRepository.AddAsync(
                new PlayerGroupLink
                {
                    PlayerProfileId = playerProfileId,
                    GroupChatId = group.Id,
                    IsPrimary = !hasPrimary,
                },
                cancellationToken);
            linkedGroupIds.Add(group.Id);
            hasPrimary = true;
            changed = true;
        }

        if (changed)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ApplicationConflictException)
            {
                // Best-effort seed: a concurrent first-time read may have inserted the same link (or
                // primary) first and tripped a unique index. Our database still holds a consistent
                // linkage, so swallow and return the stored state below rather than 500 the read.
            }
        }
    }

    private static GroupSummary ToSummary(PlayerGroupReadModel link) =>
        new(link.GroupChatId, link.ExternalId, link.GroupName, link.MemberCount, IsLinked: true, link.IsPrimary);
}

/// <summary>
/// Links the current player to a group chat in our database. This performs no external write: the
/// Pickup Pal API is read-only, so our database is the authority for the linkage.
/// </summary>
public sealed class LinkPlayerToGroupCommandHandler(
    IValidator<LinkPlayerToGroupCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGroupClient groupClient,
    IGroupChatRepository groupChatRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IUnitOfWork unitOfWork)
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

        if (!await playerGroupLinkRepository.ExistsAsync(profile.Id, group.Id, cancellationToken))
        {
            var existingLinks = await playerGroupLinkRepository.ListByPlayerAsync(profile.Id, cancellationToken);
            await playerGroupLinkRepository.AddAsync(
                new PlayerGroupLink
                {
                    PlayerProfileId = profile.Id,
                    GroupChatId = group.Id,
                    IsPrimary = existingLinks.All(link => !link.IsPrimary),
                },
                cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ApplicationConflictException)
            {
                // A concurrent link for the same player won the race and the link already exists.
                // Linking is idempotent, so return the current stored state rather than surfacing a
                // conflict error the user cannot act on.
            }
        }

        var links = await playerGroupLinkRepository.ListPlayerGroupsAsync(profile.Id, cancellationToken);
        return new MyGroupsResult(
            IsLinked: links.Count > 0,
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
