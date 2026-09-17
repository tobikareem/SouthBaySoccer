using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Groups;

/// <summary>
/// The membership state machine shared by every writer: one <see cref="PlayerGroupLink"/> row per
/// (player, group) pair that moves between Pending, Approved, Declined, and Removed. A declined
/// or removed row is reactivated by a later request rather than duplicated, so the unique active
/// index holds and the audit stamps show the last decision. Pickup Pal is only ever read (the
/// <c>/linked</c> cross-check); nothing here writes to it.
/// </summary>
public sealed class GroupMembershipService(
    IClock clock,
    IPickupPalGroupClient groupClient,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Requests membership in each group. A group Pickup Pal already lists the player in is
    /// approved at once (source WhatsApp); any other becomes Pending (source Request). Rows that
    /// are already Pending or Approved are left untouched, so the call is idempotent.
    /// </summary>
    public async Task RequestAsync(
        PlayerProfile profile,
        IReadOnlyList<GroupChat> groups,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var whatsAppExternalIds = await ListWhatsAppGroupExternalIdsAsync(profile, cancellationToken);
        var rowsByGroupId = (await playerGroupLinkRepository.ListByPlayerAsync(profile.Id, cancellationToken))
            .ToDictionary(row => row.GroupChatId);
        var hasPrimary = rowsByGroupId.Values.Any(row => row.IsPrimary);
        var nowUtc = clock.UtcNow;
        var changed = false;

        foreach (var group in groups)
        {
            var listedOnWhatsApp = whatsAppExternalIds.Contains(group.ExternalId);
            if (rowsByGroupId.TryGetValue(group.Id, out var row))
            {
                if (row.Status is GroupMembershipStatus.Approved or GroupMembershipStatus.Pending)
                {
                    continue;
                }

                // Declined / Removed: the player asks again. Reactivate the same row.
                Reset(row, nowUtc);
                if (listedOnWhatsApp)
                {
                    Approve(row, nowUtc, approvedBy: null, GroupMembershipSource.WhatsApp, ref hasPrimary);
                }
                else
                {
                    row.Status = GroupMembershipStatus.Pending;
                    row.Source = GroupMembershipSource.Request;
                }

                playerGroupLinkRepository.Update(row);
                changed = true;
                continue;
            }

            var created = new PlayerGroupLink
            {
                PlayerProfileId = profile.Id,
                GroupChatId = group.Id,
                RequestedAtUtc = nowUtc,
                Status = GroupMembershipStatus.Pending,
                Source = GroupMembershipSource.Request,
            };
            if (listedOnWhatsApp)
            {
                Approve(created, nowUtc, approvedBy: null, GroupMembershipSource.WhatsApp, ref hasPrimary);
            }

            await playerGroupLinkRepository.AddAsync(created, cancellationToken);
            rowsByGroupId[group.Id] = created;
            changed = true;
        }

        if (changed)
        {
            await SaveIdempotentAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Mirrors the WhatsApp groups Pickup Pal lists the player in as approved memberships, for
    /// pairs that have no row at all. A row in any other status (including Removed by an admin)
    /// is never touched here: a removal must not be undone by the next sign-in read.
    /// </summary>
    public async Task SeedFromWhatsAppAsync(
        PlayerProfile profile,
        IReadOnlyList<GroupChat> whatsAppGroups,
        CancellationToken cancellationToken)
    {
        if (whatsAppGroups.Count == 0)
        {
            return;
        }

        var rows = await playerGroupLinkRepository.ListByPlayerAsync(profile.Id, cancellationToken);
        var knownGroupIds = rows.Select(row => row.GroupChatId).ToHashSet();
        var hasPrimary = rows.Any(row => row.IsPrimary);
        var nowUtc = clock.UtcNow;
        var changed = false;

        foreach (var group in whatsAppGroups.Where(group => !knownGroupIds.Contains(group.Id)))
        {
            var created = new PlayerGroupLink
            {
                PlayerProfileId = profile.Id,
                GroupChatId = group.Id,
                RequestedAtUtc = nowUtc,
            };
            Approve(created, nowUtc, approvedBy: null, GroupMembershipSource.WhatsApp, ref hasPrimary);
            await playerGroupLinkRepository.AddAsync(created, cancellationToken);
            knownGroupIds.Add(group.Id);
            changed = true;
        }

        if (changed)
        {
            await SaveIdempotentAsync(cancellationToken);
        }
    }

    /// <summary>A super admin adds a known player straight in as an approved member (or approves a pending / re-admits a removed one).</summary>
    public async Task AddDirectlyAsync(
        Guid playerProfileId,
        Guid groupChatId,
        Guid actingPlayerProfileId,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var row = await playerGroupLinkRepository.FindMembershipAsync(playerProfileId, groupChatId, cancellationToken);
        var hasPrimary = (await playerGroupLinkRepository.ListByPlayerAsync(playerProfileId, cancellationToken))
            .Any(existing => existing.IsPrimary);
        if (row is null)
        {
            row = new PlayerGroupLink { PlayerProfileId = playerProfileId, GroupChatId = groupChatId, RequestedAtUtc = nowUtc };
            Approve(row, nowUtc, actingPlayerProfileId, GroupMembershipSource.SuperAdmin, ref hasPrimary);
            await playerGroupLinkRepository.AddAsync(row, cancellationToken);
        }
        else if (row.Status != GroupMembershipStatus.Approved)
        {
            Reset(row, nowUtc);
            Approve(row, nowUtc, actingPlayerProfileId, GroupMembershipSource.SuperAdmin, ref hasPrimary);
            playerGroupLinkRepository.Update(row);
        }
        else
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Approves a pending request. Approved rows are left as they are; anything else is a conflict.</summary>
    public async Task ApproveAsync(PlayerGroupLink row, Guid actingPlayerProfileId, CancellationToken cancellationToken)
    {
        if (row.Status == GroupMembershipStatus.Approved)
        {
            return;
        }

        if (row.Status != GroupMembershipStatus.Pending)
        {
            throw new ApplicationConflictException("Only a pending request can be approved.");
        }

        var hasPrimary = (await playerGroupLinkRepository.ListByPlayerAsync(row.PlayerProfileId, cancellationToken))
            .Any(existing => existing.IsPrimary && existing.Id != row.Id);
        Approve(row, clock.UtcNow, actingPlayerProfileId, row.Source, ref hasPrimary);
        playerGroupLinkRepository.Update(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Declines a pending request.</summary>
    public async Task DeclineAsync(PlayerGroupLink row, Guid actingPlayerProfileId, CancellationToken cancellationToken)
    {
        if (row.Status == GroupMembershipStatus.Declined)
        {
            return;
        }

        if (row.Status != GroupMembershipStatus.Pending)
        {
            throw new ApplicationConflictException("Only a pending request can be declined.");
        }

        End(row, GroupMembershipStatus.Declined, actingPlayerProfileId);
        playerGroupLinkRepository.Update(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes an approved member (an admin removing them, or the player leaving). Idempotent.</summary>
    public async Task RemoveAsync(PlayerGroupLink row, Guid actingPlayerProfileId, CancellationToken cancellationToken)
    {
        if (row.Status == GroupMembershipStatus.Removed)
        {
            return;
        }

        if (row.Status != GroupMembershipStatus.Approved)
        {
            throw new ApplicationConflictException("Only an approved member can be removed.");
        }

        End(row, GroupMembershipStatus.Removed, actingPlayerProfileId);
        playerGroupLinkRepository.Update(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Appoints or revokes a group admin. Only an approved member can hold the role.</summary>
    public async Task SetRoleAsync(PlayerGroupLink row, GroupMemberRole role, CancellationToken cancellationToken)
    {
        if (row.Status != GroupMembershipStatus.Approved)
        {
            throw new ApplicationConflictException("Only an approved member can be a group admin.");
        }

        if (row.Role == role)
        {
            return;
        }

        row.Role = role;
        playerGroupLinkRepository.Update(row);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // The external read is a best-effort cross-check; a Pickup Pal failure only means "not
    // listed", which leaves the request Pending for a group admin. Only external ids cross this
    // boundary and none is logged.
    private async Task<HashSet<string>> ListWhatsAppGroupExternalIdsAsync(PlayerProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.PickupPalUserId))
        {
            return [];
        }

        try
        {
            var groups = await groupClient.GetLinkedGroupsAsync(profile.PickupPalUserId, cancellationToken);
            return groups
                .Where(group => !string.IsNullOrWhiteSpace(group.ExternalId))
                .Select(group => group.ExternalId)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void Approve(
        PlayerGroupLink row,
        DateTime nowUtc,
        Guid? approvedBy,
        GroupMembershipSource source,
        ref bool hasPrimary)
    {
        row.Status = GroupMembershipStatus.Approved;
        row.Source = source;
        row.ApprovedAtUtc = nowUtc;
        row.ApprovedByPlayerProfileId = approvedBy;
        if (!hasPrimary)
        {
            row.IsPrimary = true;
            hasPrimary = true;
        }
    }

    private static void Reset(PlayerGroupLink row, DateTime nowUtc)
    {
        row.RequestedAtUtc = nowUtc;
        row.ApprovedAtUtc = null;
        row.ApprovedByPlayerProfileId = null;
        row.RemovedAtUtc = null;
        row.RemovedByPlayerProfileId = null;
        row.Role = GroupMemberRole.Member;
        row.IsPrimary = false;
    }

    private void End(PlayerGroupLink row, GroupMembershipStatus status, Guid actingPlayerProfileId)
    {
        row.Status = status;
        row.Role = GroupMemberRole.Member;
        row.IsPrimary = false;
        row.RemovedAtUtc = clock.UtcNow;
        row.RemovedByPlayerProfileId = actingPlayerProfileId;
    }

    private async Task SaveIdempotentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConflictException)
        {
            // A concurrent request for the same player won the race on the unique (player, group)
            // or primary index. The stored state is what the caller re-reads, so this is idempotent.
        }
    }
}
