using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMembershipApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerGroupLinks_GroupChatId",
                table: "PlayerGroupLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "PlayerGroupLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByPlayerProfileId",
                table: "PlayerGroupLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAtUtc",
                table: "PlayerGroupLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemovedByPlayerProfileId",
                table: "PlayerGroupLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "PlayerGroupLinks",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Column defaults are the fail-closed values (a row nobody classified is a pending
            // request); the application always writes explicit values.
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "PlayerGroupLinks",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PlayerGroupLinks",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Request");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PlayerGroupLinks",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            // Backfill: every link that existed before approval was introduced was mirrored from
            // (or self-linked against) WhatsApp membership, so it is an approved member from the
            // moment it was created.
            migrationBuilder.Sql(
                """
                UPDATE [PlayerGroupLinks]
                SET [Status] = 'Approved',
                    [Role] = 'Member',
                    [Source] = 'WhatsApp',
                    [RequestedAtUtc] = [CreatedAt],
                    [ApprovedAtUtc] = [CreatedAt];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGroupLinks_GroupChatId_Status",
                table: "PlayerGroupLinks",
                columns: new[] { "GroupChatId", "Status" },
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerGroupLinks_GroupChatId_Status",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "ApprovedByPlayerProfileId",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "RemovedAtUtc",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "RemovedByPlayerProfileId",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PlayerGroupLinks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PlayerGroupLinks");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGroupLinks_GroupChatId",
                table: "PlayerGroupLinks",
                column: "GroupChatId");
        }
    }
}
