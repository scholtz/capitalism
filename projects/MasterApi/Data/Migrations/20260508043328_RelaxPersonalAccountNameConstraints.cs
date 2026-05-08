using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelaxPersonalAccountNameConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerAccounts_PersonalAccountName",
                table: "PlayerAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccounts_PersonalAccountName",
                table: "PlayerAccounts",
                column: "PersonalAccountName",
                unique: true);
        }
    }
}
