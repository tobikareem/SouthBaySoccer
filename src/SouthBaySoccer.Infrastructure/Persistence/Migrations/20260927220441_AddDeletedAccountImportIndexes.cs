using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAccountImportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_Deleted_PhoneNumberHash",
                table: "PlayerProfiles",
                column: "PhoneNumberHash",
                filter: "[IsDeleted] = 1 AND [IdentityUserId] IS NOT NULL AND [PhoneNumberHash] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "IdentityUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_Deleted_PickupPalUserId",
                table: "PlayerProfiles",
                column: "PickupPalUserId",
                filter: "[IsDeleted] = 1 AND [IdentityUserId] IS NOT NULL AND [PickupPalUserId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "IdentityUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_Deleted_WhatsAppJidHash",
                table: "PlayerProfiles",
                column: "WhatsAppJidHash",
                filter: "[IsDeleted] = 1 AND [IdentityUserId] IS NOT NULL AND [WhatsAppJidHash] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "IdentityUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_Deleted_PhoneNumberHash",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_Deleted_PickupPalUserId",
                table: "PlayerProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_Deleted_WhatsAppJidHash",
                table: "PlayerProfiles");
        }
    }
}
