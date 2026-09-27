using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionPickupPalGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupChatId",
                table: "Sessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupPalGameId",
                table: "Sessions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupPalOrigin",
                table: "Sessions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "PickupPalSyncError",
                table: "Sessions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupPalSyncStatus",
                table: "Sessions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<DateTime>(
                name: "PickupPalSyncedAtUtc",
                table: "Sessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_GroupChatId",
                table: "Sessions",
                column: "GroupChatId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_GroupChats_GroupChatId",
                table: "Sessions",
                column: "GroupChatId",
                principalTable: "GroupChats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill: sessions the active-games import created carry the game id in their
            // occurrence key ("pickuppal:{gameId}"); Pickup Pal owns those, so they are Imported.
            migrationBuilder.Sql(
                """
                UPDATE [Sessions]
                SET [PickupPalOrigin] = N'Imported',
                    [PickupPalGameId] = SUBSTRING([OccurrenceKey], 11, LEN([OccurrenceKey]) - 10)
                WHERE [OccurrenceKey] LIKE N'pickuppal:_%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_GroupChats_GroupChatId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_GroupChatId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "GroupChatId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PickupPalGameId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PickupPalOrigin",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PickupPalSyncError",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PickupPalSyncStatus",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PickupPalSyncedAtUtc",
                table: "Sessions");
        }
    }
}
