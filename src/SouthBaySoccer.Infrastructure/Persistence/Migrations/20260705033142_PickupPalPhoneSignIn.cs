using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SouthBaySoccer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PickupPalPhoneSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PickupPalUserId",
                table: "PlayerProfiles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfiles_PickupPalUserId",
                table: "PlayerProfiles",
                column: "PickupPalUserId",
                unique: true,
                filter: "[PickupPalUserId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerProfiles_PickupPalUserId",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "PickupPalUserId",
                table: "PlayerProfiles");
        }
    }
}
