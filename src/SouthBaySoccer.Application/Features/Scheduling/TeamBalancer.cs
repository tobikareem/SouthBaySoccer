using System;
using System.Collections.Generic;
using System.Linq;

namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>One team entering the balance: identity, its fixed captain, and its exact target size.</summary>
/// <param name="TeamId">The match team receiving players.</param>
/// <param name="CaptainPlayerProfileId">The captain, who stays on this team no matter what.</param>
/// <param name="Cap">The team's exact final size including the captain.</param>
public sealed record TeamBalancerSeed(Guid TeamId, Guid CaptainPlayerProfileId, int Cap);

/// <summary>A player entering the balance with their shrunken skill score.</summary>
public sealed record TeamBalancerPlayer(Guid PlayerProfileId, decimal Score);

/// <summary>
/// Deals a session's eligible roster into balanced teams, adapted from the classic pickup-soccer
/// balancing recipe: seed each team with its (fixed, ranked) captain, snake-draft the remaining
/// players by descending score in captain-rank order, then run a bounded best-swap improvement loop
/// that shrinks the spread between the strongest and weakest team averages. Pure and deterministic:
/// the same inputs and seed always produce the same deal; the seed only breaks score ties.
/// </summary>
public static class TeamBalancer
{
    /// <summary>Bound on improvement passes; each strictly reduces the spread, so this is a backstop.</summary>
    private const int MaxImprovementIterations = 50;

    /// <summary>Spread below which teams are considered balanced and swapping stops.</summary>
    private const decimal SpreadEpsilon = 0.001m;

    /// <summary>
    /// Produces the full team-to-players deal (captains included). Throws
    /// <see cref="ArgumentException"/> only on caller programming errors — the command handler
    /// validates user-facing preconditions before calling.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Balance(
        IReadOnlyList<TeamBalancerSeed> teams,
        IReadOnlyList<TeamBalancerPlayer> players,
        int seed)
    {
        if (teams.Count == 0)
        {
            throw new ArgumentException("At least one team is required.", nameof(teams));
        }

        if (teams.Any(team => team.Cap < 1))
        {
            throw new ArgumentException("Every team cap must include at least the captain.", nameof(teams));
        }

        if (players.Count > teams.Sum(team => team.Cap))
        {
            throw new ArgumentException("More players than the teams can hold.", nameof(players));
        }

        var scoreById = players.ToDictionary(player => player.PlayerProfileId, player => player.Score);
        var captainIds = teams.Select(team => team.CaptainPlayerProfileId).ToHashSet();
        if (captainIds.Count != teams.Count || !captainIds.All(scoreById.ContainsKey))
        {
            throw new ArgumentException("Every team needs a distinct captain present in the player list.", nameof(teams));
        }

        var states = teams
            .Select(team => new TeamState(team.TeamId, team.CaptainPlayerProfileId, team.Cap, scoreById[team.CaptainPlayerProfileId]))
            .ToArray();

        // Sort by id first so the deal is independent of input ordering, then shuffle with the
        // seeded generator so equal scores land in seed-dependent order, then stable-sort by score.
        var pool = players
            .Where(player => !captainIds.Contains(player.PlayerProfileId))
            .OrderBy(player => player.PlayerProfileId)
            .ToArray();
        Shuffle(pool, new Random(seed));
        var ordered = pool.OrderByDescending(player => player.Score).ToArray();

        SnakeFill(states, ordered);
        Improve(states);

        return states.ToDictionary(
            state => state.TeamId,
            state => (IReadOnlyList<Guid>)state.Players.Select(player => player.PlayerProfileId).ToArray());
    }

    // Strict snake in captain-rank order (1..N then N..1), skipping teams that reached their cap:
    // the 1st captain gets the best remaining player, the last captain gets two in a row.
    private static void SnakeFill(TeamState[] states, IReadOnlyList<TeamBalancerPlayer> ordered)
    {
        var descending = false;
        var next = 0;
        while (next < ordered.Count)
        {
            var sequence = descending ? states.Reverse() : states;
            var progressed = false;
            foreach (var state in sequence)
            {
                if (next >= ordered.Count)
                {
                    break;
                }

                if (state.Players.Count >= state.Cap)
                {
                    continue;
                }

                state.Add(ordered[next]);
                next++;
                progressed = true;
            }

            if (!progressed)
            {
                // Defensive: capacity was validated up front, so a full pass with no placement
                // can only mean every team is at cap and the loop is done.
                break;
            }

            descending = !descending;
        }
    }

    // Repeatedly swap the single best non-captain pair between the strongest and weakest teams
    // (by average score) while it narrows the spread. Equal-size swaps keep every cap intact.
    private static void Improve(TeamState[] states)
    {
        if (states.Length < 2)
        {
            return;
        }

        for (var iteration = 0; iteration < MaxImprovementIterations; iteration++)
        {
            var strongest = states.MaxBy(state => state.Average)!;
            var weakest = states.MinBy(state => state.Average)!;
            var spread = strongest.Average - weakest.Average;
            if (ReferenceEquals(strongest, weakest) || spread <= SpreadEpsilon)
            {
                return;
            }

            TeamBalancerPlayer? bestFromStrong = null;
            TeamBalancerPlayer? bestFromWeak = null;
            var bestSpread = spread;
            foreach (var strong in strongest.Players.Where(player => player.PlayerProfileId != strongest.CaptainPlayerProfileId))
            {
                foreach (var weak in weakest.Players.Where(player => player.PlayerProfileId != weakest.CaptainPlayerProfileId))
                {
                    var strongAverage = strongest.AverageAfterSwap(strong, weak);
                    var weakAverage = weakest.AverageAfterSwap(weak, strong);
                    var candidateSpread = SpreadAcross(states, strongest, weakest, strongAverage, weakAverage);
                    if (candidateSpread < bestSpread)
                    {
                        bestSpread = candidateSpread;
                        bestFromStrong = strong;
                        bestFromWeak = weak;
                    }
                }
            }

            if (bestFromStrong is null || bestFromWeak is null)
            {
                return;
            }

            strongest.Replace(bestFromStrong, bestFromWeak);
            weakest.Replace(bestFromWeak, bestFromStrong);
        }
    }

    private static decimal SpreadAcross(
        TeamState[] states,
        TeamState swappedA,
        TeamState swappedB,
        decimal averageA,
        decimal averageB)
    {
        var max = decimal.MinValue;
        var min = decimal.MaxValue;
        foreach (var state in states)
        {
            var average = ReferenceEquals(state, swappedA) ? averageA
                : ReferenceEquals(state, swappedB) ? averageB
                : state.Average;
            max = Math.Max(max, average);
            min = Math.Min(min, average);
        }

        return max - min;
    }

    private static void Shuffle<T>(T[] items, Random random)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Derives the deterministic deal seed from the match and the caller's attempt number.
    /// FNV-1a over the match id bytes (never <see cref="Guid.GetHashCode"/>, whose stability is
    /// undocumented) XOR the attempt, so "balance again" yields a different but repeatable deal.
    /// </summary>
    public static int DeriveSeed(Guid matchId, int attempt)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var value in matchId.ToByteArray())
            {
                hash = (hash ^ value) * 16777619u;
            }

            return (int)hash ^ attempt;
        }
    }

    private sealed class TeamState(Guid teamId, Guid captainPlayerProfileId, int cap, decimal captainScore)
    {
        private readonly List<TeamBalancerPlayer> players = [new(captainPlayerProfileId, captainScore)];
        private decimal total = captainScore;

        public Guid TeamId { get; } = teamId;

        public Guid CaptainPlayerProfileId { get; } = captainPlayerProfileId;

        public int Cap { get; } = cap;

        public IReadOnlyList<TeamBalancerPlayer> Players => players;

        public decimal Average => total / players.Count;

        public void Add(TeamBalancerPlayer player)
        {
            players.Add(player);
            total += player.Score;
        }

        public decimal AverageAfterSwap(TeamBalancerPlayer outgoing, TeamBalancerPlayer incoming) =>
            (total - outgoing.Score + incoming.Score) / players.Count;

        public void Replace(TeamBalancerPlayer outgoing, TeamBalancerPlayer incoming)
        {
            players.Remove(outgoing);
            players.Add(incoming);
            total += incoming.Score - outgoing.Score;
        }
    }
}
