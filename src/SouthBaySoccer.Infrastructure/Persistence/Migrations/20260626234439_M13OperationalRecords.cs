using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M13OperationalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "RefreshTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddressHash",
                table: "RefreshTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReuseDetectedAtUtc",
                table: "RefreshTokens",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "RefreshTokens",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByRefreshTokenId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgentHash",
                table: "RefreshTokens",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_IdentityUserId_FamilyId_ReuseDetectedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "IdentityUserId", "FamilyId", "ReuseDetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedByRefreshTokenId",
                table: "RefreshTokens",
                column: "ReplacedByRefreshTokenId",
                unique: true,
                filter: "[ReplacedByRefreshTokenId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_RevokedByRefreshTokenId",
                table: "RefreshTokens",
                column: "RevokedByRefreshTokenId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_ReplacedByNotSelf",
                table: "RefreshTokens",
                sql: "[ReplacedByRefreshTokenId] IS NULL OR [ReplacedByRefreshTokenId] <> [Id]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_ReplacedOnlyAfterConsumption",
                table: "RefreshTokens",
                sql: "[ReplacedByRefreshTokenId] IS NULL OR [ConsumedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_ReusedOnlyAfterConsumption",
                table: "RefreshTokens",
                sql: "[ReuseDetectedAtUtc] IS NULL OR [ConsumedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_RevocationReasonRequiresRevocation",
                table: "RefreshTokens",
                sql: "[RevocationReason] IS NULL OR [RevokedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshTokens_RevokedByNotSelf",
                table: "RefreshTokens",
                sql: "[RevokedByRefreshTokenId] IS NULL OR [RevokedByRefreshTokenId] <> [Id]");

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedByRefreshTokenId",
                table: "RefreshTokens",
                column: "ReplacedByRefreshTokenId",
                principalTable: "RefreshTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_RevokedByRefreshTokenId",
                table: "RefreshTokens",
                column: "RevokedByRefreshTokenId",
                principalTable: "RefreshTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedByRefreshTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_RevokedByRefreshTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_IdentityUserId_FamilyId_ReuseDetectedAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ReplacedByRefreshTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_RevokedByRefreshTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_ReplacedByNotSelf",
                table: "RefreshTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_ReplacedOnlyAfterConsumption",
                table: "RefreshTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_ReusedOnlyAfterConsumption",
                table: "RefreshTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_RevocationReasonRequiresRevocation",
                table: "RefreshTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RefreshTokens_RevokedByNotSelf",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "IpAddressHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReuseDetectedAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RevokedByRefreshTokenId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "UserAgentHash",
                table: "RefreshTokens");
        }
    }
}
