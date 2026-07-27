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
                name: "IX_PlayerProfiles_NormalizedDisplayName",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_MatchId_PlayerProfileId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchStats_PlayerProfileId",
                table: "PlayerMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_PlayerProfileId_EventType",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_MatchAwards_PlayerProfileId_AwardType_MatchId",
                table: "MatchAwards");

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
                name: "IX_MatchAwards_AwardType_PlayerProfileId_MatchId",
                table: "MatchAwards",
                columns: new[] { "AwardType", "PlayerProfileId", "MatchId" },
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_MatchAwards_AwardType_PlayerProfileId_MatchId",
                table: "MatchAwards");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_NormalizedDisplayName",
                table: "PlayerProfiles",
                column: "NormalizedDisplayName",
                filter: "[IsDeleted] = 0");

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
                name: "IX_MatchEvents_PlayerProfileId_EventType",
                table: "MatchEvents",
                columns: new[] { "PlayerProfileId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchAwards_PlayerProfileId_AwardType_MatchId",
                table: "MatchAwards",
                columns: new[] { "PlayerProfileId", "AwardType", "MatchId" },
                filter: "[IsDeleted] = 0");
        }
    }
}
