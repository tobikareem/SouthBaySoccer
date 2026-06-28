using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M8StatsReviewCloseout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "MatchEvents",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "MatchEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "MatchEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByPlayerProfileId",
                table: "MatchEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByPlayerProfileId",
                table: "MatchEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProfileStatReassignmentAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceGuestPlayerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetPlayerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AffectedCount = table.Column<int>(type: "int", nullable: false),
                    ReassignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileStatReassignmentAudits", x => x.Id);
                    table.CheckConstraint("CK_ProfileStatReassignmentAudits_AffectedCount", "[AffectedCount] >= 0");
                    table.ForeignKey(
                        name: "FK_ProfileStatReassignmentAudits_PlayerProfiles_SourceGuestPlayerProfileId",
                        column: x => x.SourceGuestPlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileStatReassignmentAudits_PlayerProfiles_TargetPlayerProfileId",
                        column: x => x.TargetPlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_MatchId_ReviewStatus",
                table: "MatchEvents",
                columns: new[] { "MatchId", "ReviewStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_ReviewedByPlayerProfileId",
                table: "MatchEvents",
                column: "ReviewedByPlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchEvents_SubmittedByPlayerProfileId",
                table: "MatchEvents",
                column: "SubmittedByPlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileStatReassignmentAudits_SourceGuestPlayerProfileId_TargetPlayerProfileId_ReassignedAtUtc",
                table: "ProfileStatReassignmentAudits",
                columns: new[] { "SourceGuestPlayerProfileId", "TargetPlayerProfileId", "ReassignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileStatReassignmentAudits_TargetPlayerProfileId",
                table: "ProfileStatReassignmentAudits",
                column: "TargetPlayerProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchEvents_PlayerProfiles_ReviewedByPlayerProfileId",
                table: "MatchEvents",
                column: "ReviewedByPlayerProfileId",
                principalTable: "PlayerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchEvents_PlayerProfiles_SubmittedByPlayerProfileId",
                table: "MatchEvents",
                column: "SubmittedByPlayerProfileId",
                principalTable: "PlayerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchEvents_PlayerProfiles_ReviewedByPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_MatchEvents_PlayerProfiles_SubmittedByPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropTable(
                name: "ProfileStatReassignmentAudits");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_MatchId_ReviewStatus",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_ReviewedByPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropIndex(
                name: "IX_MatchEvents_SubmittedByPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedByPlayerProfileId",
                table: "MatchEvents");

            migrationBuilder.DropColumn(
                name: "SubmittedByPlayerProfileId",
                table: "MatchEvents");
        }
    }
}

