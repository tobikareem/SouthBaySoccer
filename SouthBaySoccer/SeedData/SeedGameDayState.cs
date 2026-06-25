using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.SeedData;

public sealed class SeedGameDayState
{
    private readonly Lock syncRoot = new();
    private readonly Guid[] teamIds =
    [
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        Guid.Parse("60000000-0000-0000-0000-000000000002"),
        Guid.Parse("60000000-0000-0000-0000-000000000003"),
        Guid.Parse("60000000-0000-0000-0000-000000000004")
    ];

    private HashSet<Guid> checkedInPlayerIds = [];
    private int captainCount;
    private List<Guid> captainIds = [];
    private Dictionary<Guid, List<Guid>> teamPlayerIds = [];
    private Dictionary<Guid, TeamResultDto> teamResults = [];
    private List<PendingStatApprovalDto> approvals = [];
    private List<RecentFormUpdateDto> recentFormUpdates = [];
    private bool isPublished;

    public SeedGameDayState()
    {
        Reset();
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            checkedInPlayerIds = SeedFixtures.Rosters[SeedFixtures.MarinaSessionId]
                .Going
                .Skip(1)
                .Take(10)
                .Select(entry => entry.Player.Id)
                .ToHashSet();
            captainCount = 2;
            captainIds = [SeedFixtures.Players[1].Id, SeedFixtures.Players[2].Id];
            teamPlayerIds = ActiveTeamIds().ToDictionary(teamId => teamId, _ => new List<Guid>());
            teamPlayerIds[teamIds[0]].AddRange([SeedFixtures.CurrentPlayerId, SeedFixtures.Players[2].Id, SeedFixtures.Players[3].Id]);
            teamPlayerIds[teamIds[1]].AddRange([SeedFixtures.Players[1].Id, SeedFixtures.Players[4].Id]);
            teamResults = ActiveTeamIds().ToDictionary(
                teamId => teamId,
                teamId => new TeamResultDto(teamId, TeamName(teamId), 0, 0, 0));
            approvals =
            [
                Approval(1, 2, 1, StatApprovalStatus.Pending),
                Approval(2, 1, 2, StatApprovalStatus.Pending),
                Approval(3, 0, 1, StatApprovalStatus.NeedsReview)
            ];
            recentFormUpdates = [];
            isPublished = false;
        }
    }

    public GameDayContextDto GetContext()
    {
        lock (syncRoot)
        {
            var isCheckedIn = checkedInPlayerIds.Contains(SeedFixtures.CurrentPlayerId);
            var status = isCheckedIn ? GameDayStatus.CheckedIn : GameDayStatus.Open;

            return new GameDayContextDto(
                SeedFixtures.MarinaSessionId,
                SeedFixtures.FeaturedMatchId,
                "Game Day",
                "Marina Field",
                "Today",
                "7:40 PM",
                "7:30 PM - 7:45 PM",
                "closes 7:45 PM",
                status,
                isCheckedIn ? "Checked in" : "Open",
                !isCheckedIn,
                isCheckedIn ? "Checked in" : "Check in at field",
                null,
                "Going",
                true,
                isCheckedIn,
                SeedFixtures.Rosters[SeedFixtures.MarinaSessionId].Going.Count,
                checkedInPlayerIds.Count,
                0,
                true,
                captainIds.Contains(SeedFixtures.CurrentPlayerId),
                captainIds.Contains(SeedFixtures.CurrentPlayerId));
        }
    }

    public GameDayContextDto GetClosedContext()
    {
        var context = GetContext();
        return context with
        {
            Status = GameDayStatus.Closed,
            StatusLabel = "Closed",
            IsSelfCheckInAvailable = false,
            PrimaryActionText = "GameAdmin override required",
            BlockReason = "Check-in closed at 7:45 PM. A GameAdmin override is required for late arrivals."
        };
    }

    public ClientCommandResult CheckIn(Guid sessionId)
    {
        lock (syncRoot)
        {
            if (sessionId != SeedFixtures.MarinaSessionId)
            {
                return ClientCommandResult.Failure("session_not_found", "The session was not found.");
            }

            checkedInPlayerIds.Add(SeedFixtures.CurrentPlayerId);
            return ClientCommandResult.Success;
        }
    }

    public CaptainAssignmentDto GetCaptainAssignment(Guid sessionId)
    {
        lock (syncRoot)
        {
            return new CaptainAssignmentDto(
                sessionId,
                SeedFixtures.FeaturedMatchId,
                captainCount,
                [2, 3, 4],
                Array.AsReadOnly(captainIds.ToArray()),
                CheckedInPlayers());
        }
    }

    public ClientCommandResult AssignCaptains(Guid sessionId, int count, IReadOnlyList<Guid> selectedCaptainIds)
    {
        lock (syncRoot)
        {
            if (sessionId != SeedFixtures.MarinaSessionId)
            {
                return ClientCommandResult.Failure("session_not_found", "The session was not found.");
            }

            if (count is < 2 or > 4 || selectedCaptainIds.Count != count)
            {
                return ClientCommandResult.Failure("invalid_captains", "Select one captain per team.");
            }

            if (selectedCaptainIds.Distinct().Count() != selectedCaptainIds.Count
                || selectedCaptainIds.Any(playerId => !checkedInPlayerIds.Contains(playerId)))
            {
                return ClientCommandResult.Failure("captain_not_checked_in", "Captains must be checked in.");
            }

            captainCount = count;
            captainIds = selectedCaptainIds.ToList();
            teamPlayerIds = ActiveTeamIds().ToDictionary(
                teamId => teamId,
                teamId => new List<Guid> { CaptainId(teamId) });
            teamResults = ActiveTeamIds().ToDictionary(
                teamId => teamId,
                teamId => new TeamResultDto(teamId, TeamName(teamId), 0, 0, 0));
            isPublished = false;
            recentFormUpdates.Clear();

            return ClientCommandResult.Success;
        }
    }

    public TeamDraftDto GetTeamDraft(Guid sessionId)
    {
        lock (syncRoot)
        {
            var teams = MatchTeams();
            var currentTeam = teams.FirstOrDefault(team => team.CaptainId == SeedFixtures.CurrentPlayerId)
                ?? teams[0];

            return new TeamDraftDto(
                sessionId,
                SeedFixtures.FeaturedMatchId,
                currentTeam.TeamId,
                currentTeam.Name,
                currentTeam.CaptainName,
                currentTeam.CaptainId == SeedFixtures.CurrentPlayerId,
                isPublished,
                captainCount,
                CheckedInPlayers(),
                teams);
        }
    }

    public ClientCommandResult SaveTeamPicks(Guid sessionId, Guid teamId, IReadOnlyList<Guid> selectedPlayerIds)
    {
        lock (syncRoot)
        {
            if (!teamPlayerIds.ContainsKey(teamId))
            {
                return ClientCommandResult.Failure("team_not_found", "The team was not found.");
            }

            if (isPublished)
            {
                return ClientCommandResult.Failure("teams_locked", "Teams are locked.");
            }

            var selected = selectedPlayerIds.Append(CaptainId(teamId)).Distinct().ToArray();
            if (selected.Any(playerId => !checkedInPlayerIds.Contains(playerId)))
            {
                return ClientCommandResult.Failure("player_not_checked_in", "Only checked-in players can be assigned.");
            }

            var assignedElsewhere = teamPlayerIds
                .Where(pair => pair.Key != teamId)
                .SelectMany(pair => pair.Value)
                .ToHashSet();
            if (selected.Any(assignedElsewhere.Contains))
            {
                return ClientCommandResult.Failure("player_already_assigned", "A player can only be assigned to one team.");
            }

            teamPlayerIds[teamId] = selected.ToList();
            return ClientCommandResult.Success;
        }
    }

    public PostGameApprovalDto GetPostGameApproval(Guid sessionId)
    {
        lock (syncRoot)
        {
            return new PostGameApprovalDto(
                sessionId,
                SeedFixtures.FeaturedMatchId,
                captainIds.Contains(SeedFixtures.CurrentPlayerId),
                isPublished,
                approvals.Any(item => item.Status == StatApprovalStatus.NeedsReview),
                captainCount,
                Array.AsReadOnly(teamResults.Values.ToArray()),
                Array.AsReadOnly(approvals.ToArray()));
        }
    }

    public ClientCommandResult ApproveStat(Guid submissionId)
    {
        lock (syncRoot)
        {
            var index = approvals.FindIndex(item => item.SubmissionId == submissionId);
            if (index < 0)
            {
                return ClientCommandResult.Failure("submission_not_found", "The submission was not found.");
            }

            if (approvals[index].Status == StatApprovalStatus.NeedsReview)
            {
                return ClientCommandResult.Failure("needs_review", "A GameAdmin must resolve disputed stats.");
            }

            approvals[index] = approvals[index] with { Status = StatApprovalStatus.Approved };
            return ClientCommandResult.Success;
        }
    }

    public ClientCommandResult SaveTeamResult(TeamResultUpdateDto result)
    {
        lock (syncRoot)
        {
            if (result.Wins + result.Draws + result.Losses > captainCount - 1)
            {
                return ClientCommandResult.Failure("invalid_result", "Wins, draws, and losses cannot exceed teams minus one.");
            }

            if (!teamResults.TryGetValue(result.TeamId, out var existing))
            {
                return ClientCommandResult.Failure("team_not_found", "The team was not found.");
            }

            teamResults[result.TeamId] = existing with
            {
                Wins = result.Wins,
                Draws = result.Draws,
                Losses = result.Losses
            };
            return ClientCommandResult.Success;
        }
    }

    public ClientCommandResult Publish()
    {
        lock (syncRoot)
        {
            if (approvals.Any(item => item.Status == StatApprovalStatus.NeedsReview))
            {
                return ClientCommandResult.Failure("needs_review", "Resolve disputed stats before publishing.");
            }

            recentFormUpdates = teamResults
                .SelectMany(pair => teamPlayerIds[pair.Key].SelectMany(playerId => Outcomes(pair.Value)
                    .Select(result => new RecentFormUpdateDto(playerId, result))))
                .ToList();
            isPublished = true;
            return ClientCommandResult.Success;
        }
    }

    public IReadOnlyList<MatchResult> RecentFormFor(Guid playerId, IReadOnlyList<MatchResult> fallback)
    {
        lock (syncRoot)
        {
            var derived = recentFormUpdates
                .Where(item => item.PlayerId == playerId)
                .Select(item => item.Result)
                .ToArray();

            return derived.Length == 0
                ? fallback
                : Array.AsReadOnly(derived.Concat(fallback).Take(5).ToArray());
        }
    }

    private IReadOnlyList<CheckedInPlayerDto> CheckedInPlayers() =>
        Array.AsReadOnly(SeedFixtures.Rosters[SeedFixtures.MarinaSessionId]
            .Going
            .Where(entry => checkedInPlayerIds.Contains(entry.Player.Id))
            .Select(entry => new CheckedInPlayerDto(entry.Player, $"checked in - {entry.Player.Position.ToLowerInvariant()}"))
            .ToArray());

    private IReadOnlyList<MatchTeamDto> MatchTeams() =>
        Array.AsReadOnly(ActiveTeamIds()
            .Select(teamId =>
            {
                var captain = Player(CaptainId(teamId));
                return new MatchTeamDto(
                    teamId,
                    TeamName(teamId),
                    captain.Id,
                    captain.DisplayName,
                    Array.AsReadOnly(teamPlayerIds[teamId].ToArray()));
            })
            .ToArray());

    private IEnumerable<Guid> ActiveTeamIds() => teamIds.Take(captainCount);

    private Guid CaptainId(Guid teamId) => captainIds[Array.IndexOf(teamIds, teamId)];

    private static PlayerSummaryDto Player(Guid playerId) =>
        SeedFixtures.Players.First(player => player.Id == playerId);

    private static string TeamName(Guid teamId) =>
        (teamId.ToString()[^1] - '0') switch
        {
            1 => "Team Green",
            2 => "Team White",
            3 => "Team Spring",
            _ => "Team Pine"
        };

    private static PendingStatApprovalDto Approval(
        int playerIndex,
        int goals,
        int assists,
        StatApprovalStatus status) =>
        new(
            Guid.Parse($"50000000-0000-0000-0000-00000000000{playerIndex}"),
            SeedFixtures.Players[playerIndex],
            goals,
            assists,
            status);

    private static IEnumerable<MatchResult> Outcomes(TeamResultDto result) =>
        Enumerable.Repeat(MatchResult.Win, result.Wins)
            .Concat(Enumerable.Repeat(MatchResult.Draw, result.Draws))
            .Concat(Enumerable.Repeat(MatchResult.Loss, result.Losses));
}



