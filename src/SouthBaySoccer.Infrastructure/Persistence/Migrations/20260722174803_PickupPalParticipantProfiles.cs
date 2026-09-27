using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PickupPalParticipantProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppJidHash",
                table: "PlayerProfiles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerProfileId",
                table: "PickupPalGameParticipants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_PhoneNumberHash",
                table: "PlayerProfiles",
                column: "PhoneNumberHash",
                filter: "[PhoneNumberHash] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_WhatsAppJidHash",
                table: "PlayerProfiles",
                column: "WhatsAppJidHash",
                filter: "[WhatsAppJidHash] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PickupPalGameParticipants_PlayerProfileId",
                table: "PickupPalGameParticipants",
                column: "PlayerProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_PickupPalGameParticipants_PlayerProfiles_PlayerProfileId",
                table: "PickupPalGameParticipants",
                column: "PlayerProfileId",
                principalTable: "PlayerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PickupPalGameParticipants_PlayerProfiles_PlayerProfileId",
                table: "PickupPalGameParticipants");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_PhoneNumberHash",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_WhatsAppJidHash",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PickupPalGameParticipants_PlayerProfileId",
                table: "PickupPalGameParticipants");

            migrationBuilder.DropColumn(
                name: "WhatsAppJidHash",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "PlayerProfileId",
                table: "PickupPalGameParticipants");
        }
    }
}
