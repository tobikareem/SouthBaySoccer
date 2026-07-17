using System.Collections.ObjectModel;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Contracts.Stats;

namespace SouthBaySoccer.SeedData;

public static class SeedFixtures
{
    public static readonly Guid CurrentPlayerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid MarinaSessionId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid StanfordSessionId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid FeaturedMatchId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Season2026Id = Guid.Parse("40000000-0000-0000-0000-000000000001");

    private static readonly DateTime MarinaStartsAtUtc =
        new(2026, 6, 20, 16, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime StanfordStartsAtUtc =
        new(2026, 6, 27, 17, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<PlayerSummaryDto> Players { get; } = ReadOnly(
        Player(CurrentPlayerId, "Tobi Kareem", "TK", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000002", "Kola T.", "KT", "Forward"),
        Player("10000000-0000-0000-0000-000000000003", "Jide D.", "JD", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000004", "Sade M.", "SM", "Goalkeeper"),
        Player("10000000-0000-0000-0000-000000000005", "Tunde B.", "TB", "Forward", isGuest: true),
        Player("10000000-0000-0000-0000-000000000006", "Ayo N.", "AN", "Defender"),
        Player("10000000-0000-0000-0000-000000000007", "Maya R.", "MR", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000008", "Deji O.", "DO", "Defender"),
        Player("10000000-0000-0000-0000-000000000009", "Nneka E.", "NE", "Forward"),
        Player("10000000-0000-0000-0000-000000000010", "Femi A.", "FA", "Goalkeeper"),
        Player("10000000-0000-0000-0000-000000000011", "Lola P.", "LP", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000012", "Kunle S.", "KS", "Defender"),
        Player("10000000-0000-0000-0000-000000000013", "Amara C.", "AC", "Forward"),
        Player("10000000-0000-0000-0000-000000000014", "Bola G.", "BG", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000015", "Chidi I.", "CI", "Defender"),
        Player("10000000-0000-0000-0000-000000000016", "Zainab K.", "ZK", "Forward"),
        Player("10000000-0000-0000-0000-000000000017", "Emeka U.", "EU", "Defender"),
        Player("10000000-0000-0000-0000-000000000018", "Dara W.", "DW", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000019", "Ife L.", "IL", "Forward"),
        Player("10000000-0000-0000-0000-000000000020", "Segun R.", "SR", "Defender"),
        Player("10000000-0000-0000-0000-000000000021", "Ada V.", "AV", "Midfielder"),
        Player("10000000-0000-0000-0000-000000000022", "Yemi H.", "YH", "Forward"),
        Player("10000000-0000-0000-0000-000000000023", "Ngozi F.", "NF", "Goalkeeper"),
        Player("10000000-0000-0000-0000-000000000024", "Remi Q.", "RQ", "Defender"));

    public static SessionsDashboardDto Dashboard { get; } = new(
        "Saturday crew",
        "Good morning, Tobi",
        "Paid",
        new SessionSummaryDto(
            MarinaSessionId,
            "Marina Field · Saturday pickup",
            "Marina Field",
            "7v7",
            MarinaStartsAtUtc,
            "Jun 20",
            "9:00 AM",
            "You're going",
            16,
            20,
            false,
            0,
            "Next match · in 2 days"),
        new StatsPromptDto(
            FeaturedMatchId,
            "Submit your latest stats",
            "2 goals entered · captain confirmation pending"),
        "Coming up",
        "See schedule",
        ReadOnly(
            new SessionSummaryDto(
                StanfordSessionId,
                "Stanford Turf · 5v5",
                "Stanford Turf",
                "5v5",
                StanfordStartsAtUtc,
                "Jun 27",
                "10:00 AM",
                "Full",
                20,
                20,
                true,
                3,
                null)),
        CanManageSessions: true);

    public static IReadOnlyList<VenueDto> Venues { get; } = ReadOnly(
        new VenueDto(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "Marina Field",
            "South Bay · saved venue",
            true),
        new VenueDto(
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            "Stanford Turf",
            "Palo Alto",
            false),
        new VenueDto(
            Guid.Parse("50000000-0000-0000-0000-000000000003"),
            "Cuesta Park",
            "Mountain View",
            false),
        new VenueDto(
            Guid.Parse("50000000-0000-0000-0000-000000000004"),
            "Mitchell Park",
            "Palo Alto",
            false));

    public static CreateSessionDefaultsDto CreateSessionDefaults { get; } = new(
        CanManageSessions: true,
        DefaultGameDateLocal: new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Unspecified),
        DefaultStartTimeLocal: new TimeSpan(19, 40, 0),
        CheckInLeadMinutes: 10,
        CheckInCloseOffsetMinutes: 0,
        Formats: ReadOnly("5v5", "7v7", "9v9"),
        DefaultFormatIndex: 1,
        DefaultCapacity: 20,
        MinimumCapacity: 1,
        MaximumCapacity: 40,
        TeamOptions: ReadOnly("2 teams", "3 teams", "4 teams"),
        DefaultTeamIndex: 0,
        SavedVenue: Venues[0],
        FeedLabel: "Team feed",
        DefaultRsvpDeadlineLocal: new TimeSpan(18, 30, 0));

    public static IReadOnlyDictionary<Guid, SessionDetailDto> Sessions { get; } =
        new ReadOnlyDictionary<Guid, SessionDetailDto>(
            new Dictionary<Guid, SessionDetailDto>
            {
                [MarinaSessionId] = new(
                    MarinaSessionId,
                    "Saturday pickup",
                    "Marina Field",
                    "Marina Field · 7v7",
                    "7v7",
                    MarinaStartsAtUtc,
                    "Sat Jun 20 · 9:00 AM",
                    16,
                    20,
                    "closes 1d 4h",
                    false,
                    true,
                    true),
                [StanfordSessionId] = new(
                    StanfordSessionId,
                    "Saturday pickup",
                    "Stanford Turf",
                    "Stanford Turf · 5v5",
                    "5v5",
                    StanfordStartsAtUtc,
                    "Sat Jun 27 · 10:00 AM",
                    20,
                    20,
                    "waitlist open",
                    true,
                    false,
                    false)
            });

    public static IReadOnlyDictionary<Guid, RosterDto> Rosters { get; } =
        new ReadOnlyDictionary<Guid, RosterDto>(
            new Dictionary<Guid, RosterDto>
            {
                [MarinaSessionId] = new(
                    MarinaSessionId,
                    ReadOnly(
                        Going(0, true),
                        Going(1),
                        Going(2),
                        Going(3),
                        Going(5),
                        Going(6),
                        Going(7),
                        Going(8),
                        Going(9),
                        Going(10),
                        Going(11),
                        Going(12),
                        Going(13),
                        Going(14),
                        Going(15),
                        Going(16)),
                    ReadOnly(
                        Waitlisted(4, 1),
                        Waitlisted(22, 2),
                        Waitlisted(23, 3))),
                [StanfordSessionId] = new(
                    StanfordSessionId,
                    ReadOnly(
                        Going(1),
                        Going(2),
                        Going(3),
                        Going(5),
                        Going(6),
                        Going(7),
                        Going(8),
                        Going(9),
                        Going(10),
                        Going(11),
                        Going(12),
                        Going(13),
                        Going(14),
                        Going(15),
                        Going(16),
                        Going(17),
                        Going(18),
                        Going(19),
                        Going(20),
                        Going(21)),
                    ReadOnly(
                        Waitlisted(4, 1),
                        Waitlisted(22, 2),
                        Waitlisted(23, 3)))
            });

    public static MatchStatsDto MatchStats { get; } = new(
        FeaturedMatchId,
        CurrentPlayerId,
        "Sat · Marina Field",
        2,
        1,
        true,
        ReadOnly(
            new TeammateStatSubmissionDto(Players[2], 1, 2, true),
            new TeammateStatSubmissionDto(Players[3], 1, 0, false),
            new TeammateStatSubmissionDto(Players[4], 2, 0, false)));

    public static IReadOnlyList<RateableTeammateDto> RateableTeammates { get; } = ReadOnly(
        new RateableTeammateDto(Players[1], "2 goals", 9, false, false),
        new RateableTeammateDto(Players[2], "1 assist", 7, true, false),
        new RateableTeammateDto(Players[3], "clean sheet", 8, false, true));

    public static IReadOnlyDictionary<LeaderboardMetric, LeaderboardDto> Leaderboards { get; } =
        new ReadOnlyDictionary<LeaderboardMetric, LeaderboardDto>(
            new Dictionary<LeaderboardMetric, LeaderboardDto>
            {
                [LeaderboardMetric.Goals] = Ranking(
                    LeaderboardMetric.Goals,
                    "Goals rank by total; ties use fewer appearances, then assists.",
                    (1, 12m), (8, 10m), (12, 9m), (2, 8m), (4, 7m)),
                [LeaderboardMetric.Assists] = Ranking(
                    LeaderboardMetric.Assists,
                    "Assists rank by total; ties use fewer appearances.",
                    (6, 11m), (0, 9m), (2, 8m), (10, 7m), (13, 6m)),
                [LeaderboardMetric.Rating] = Ranking(
                    LeaderboardMetric.Rating,
                    "Rating includes players with at least five rated matches.",
                    (3, 8.4m), (1, 8.2m), (0, 7.8m), (6, 7.7m), (14, 7.6m)),
                [LeaderboardMetric.Mvp] = Ranking(
                    LeaderboardMetric.Mvp,
                    "MVP rank by awards; ties use fewer appearances.",
                    (1, 4m), (0, 3m), (3, 3m), (2, 2m), (8, 2m))
            });

    public static PlayerProfileDto Profile { get; } = new(
        CurrentPlayerId,
        "Tobi Kareem",
        "\"Captain\" · #8",
        "TK",
        new CareerStatsDto(24, 12, 9, 7.8m, 3, 41),
        ReadOnly(MatchResult.Win, MatchResult.Win, MatchResult.Draw, MatchResult.Win, MatchResult.Loss),
        "2 goals from Sat awaiting confirmation",
        "GameAdmin");

    private static PlayerSummaryDto Player(
        Guid id,
        string displayName,
        string initials,
        string position,
        bool isGuest = false) =>
        new(id, displayName, initials, position, isGuest);

    private static PlayerSummaryDto Player(
        string id,
        string displayName,
        string initials,
        string position,
        bool isGuest = false) =>
        Player(Guid.Parse(id), displayName, initials, position, isGuest);

    private static RosterEntryDto Going(int playerIndex, bool isCurrentPlayer = false) =>
        new(Players[playerIndex], isCurrentPlayer);

    private static WaitlistEntryDto Waitlisted(int playerIndex, int position) =>
        new(Players[playerIndex], position);

    private static LeaderboardDto Ranking(
        LeaderboardMetric metric,
        string note,
        params (int PlayerIndex, decimal Value)[] entries) =>
        new(
            Season2026Id,
            "Season 2026",
            metric,
            note,
            ReadOnly(entries.Select(
                    (entry, index) => new LeaderboardRowDto(
                        index + 1,
                        Players[entry.PlayerIndex],
                        24 - index,
                        entry.Value))
                .ToArray()));

    private static IReadOnlyList<T> ReadOnly<T>(params T[] items) =>
        Array.AsReadOnly(items);
}
