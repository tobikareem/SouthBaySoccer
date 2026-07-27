using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerfReadModelIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_MatchId_PlayerProfileId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_PlayerProfileId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_AssistPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_PlayerProfileId_EventType",
                table: "MatchEvents");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRatingVotes_RatedPlayerProfileId_MatchId",
                table: "PlayerRatingVotes",
                columns: new[] { "RatedPlayerProfileId", "MatchId" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_NormalizedDisplayName",
                table: "PlayerProfiles",
                column: "NormalizedDisplayName",
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "DisplayName", "PreferredPosition", "IsGuest" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStats_MatchId_PlayerProfileId",
                table: "PlayerMatchStats",
                columns: new[] { "MatchId", "PlayerProfileId" },
                unique: true,
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Played" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStats_PlayerProfileId_MatchId",
                table: "PlayerMatchStats",
                columns: new[] { "PlayerProfileId", "MatchId" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Played", "MinutesPlayed" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLikes_ReceiverPlayerProfileId_MatchId",
                table: "PlayerLikes",
                columns: new[] { "ReceiverPlayerProfileId", "MatchId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_AssistPlayerProfileId_EventType_ReviewStatus",
                table: "MatchEvents",
                columns: new[] { "AssistPlayerProfileId", "EventType", "ReviewStatus" },
                filter: "[IsDeleted] = 0 AND [AssistPlayerProfileId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_PlayerProfileId_EventType_ReviewStatus",
                table: "MatchEvents",
                columns: new[] { "PlayerProfileId", "EventType", "ReviewStatus" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status_SessionId",
                table: "Matches",
                columns: new[] { "Status", "SessionId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MatchAwards_AwardType_PlayerProfileId_MatchId",
                table: "MatchAwards",
                columns: new[] { "AwardType", "PlayerProfileId", "MatchId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_SessionId_Outcome",
                table: "CheckIns",
                columns: new[] { "SessionId", "Outcome" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "PlayerProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerRatingVotes_RatedPlayerProfileId_MatchId",
                table: "PlayerRatingVotes");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_NormalizedDisplayName",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_MatchId_PlayerProfileId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_PlayerProfileId_MatchId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerLikes_ReceiverPlayerProfileId_MatchId",
                table: "PlayerLikes");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_AssistPlayerProfileId_EventType_ReviewStatus",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_PlayerProfileId_EventType_ReviewStatus",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_Matches_Status_SessionId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_MatchAwards_AwardType_PlayerProfileId_MatchId",
                table: "MatchAwards");

            migrationBuilder.DropIndex(
                name: "IX_CheckIns_SessionId_Outcome",
                table: "CheckIns");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStats_MatchId_PlayerProfileId",
                table: "PlayerMatchStats",
                columns: new[] { "MatchId", "PlayerProfileId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchStats_PlayerProfileId",
                table: "PlayerMatchStats",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_AssistPlayerProfileId",
                table: "MatchEvents",
                column: "AssistPlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_PlayerProfileId_EventType",
                table: "MatchEvents",
                columns: new[] { "PlayerProfileId", "EventType" });
        }
    }
}
