using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppSignInChallenges");

            migrationBuilder.CreateTable(
                name: "PendingPhoneSignIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PickupPalUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PhoneNumberHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RememberDevice = table.Column<bool>(type: "bit", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPhoneSignIns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumberHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PhoneMasked = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreferredPosition = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TermsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PickupPalUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExternalAttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastExternalError = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingPhoneSignIns_PhoneNumberHash",
                table: "PendingPhoneSignIns",
                column: "PhoneNumberHash");

            migrationBuilder.CreateIndex(
                name: "IX_PendingPhoneSignIns_PickupPalUserId_ExpiresAtUtc",
                table: "PendingPhoneSignIns",
                columns: new[] { "PickupPalUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRegistrations_PhoneNumberHash_Status",
                table: "PlayerRegistrations",
                columns: new[] { "PhoneNumberHash", "Status" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRegistrations_PickupPalUserId",
                table: "PlayerRegistrations",
                column: "PickupPalUserId",
                filter: "[PickupPalUserId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingPhoneSignIns");

            migrationBuilder.DropTable(
                name: "PlayerRegistrations");

            migrationBuilder.CreateTable(
                name: "WhatsAppSignInChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallbackUriHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChallengeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChallengeTokenHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    MaskedPhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PhoneNumberHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PlayerProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppSignInChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppSignInChallenges_ChallengeTokenHash",
                table: "WhatsAppSignInChallenges",
                column: "ChallengeTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppSignInChallenges_PhoneNumberHash_ExpiresAtUtc",
                table: "WhatsAppSignInChallenges",
                columns: new[] { "PhoneNumberHash", "ExpiresAtUtc" });
        }
    }
}
