using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRsvpPickupPalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PickupPalSyncError",
                table: "RsvpResponses",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupPalSyncStatus",
                table: "RsvpResponses",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<DateTime>(
                name: "PickupPalSyncedAtUtc",
                table: "RsvpResponses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OutboxMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupPalSyncError",
                table: "RsvpResponses");

            migrationBuilder.DropColumn(
                name: "PickupPalSyncStatus",
                table: "RsvpResponses");

            migrationBuilder.DropColumn(
                name: "PickupPalSyncedAtUtc",
                table: "RsvpResponses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OutboxMessages");
        }
    }
}
