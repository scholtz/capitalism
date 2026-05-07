using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAccountName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonalAccountName",
                table: "PlayerAccounts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccounts_PersonalAccountName",
                table: "PlayerAccounts",
                column: "PersonalAccountName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerAccounts_PersonalAccountName",
                table: "PlayerAccounts");

            migrationBuilder.DropColumn(
                name: "PersonalAccountName",
                table: "PlayerAccounts");
        }
    }
}
