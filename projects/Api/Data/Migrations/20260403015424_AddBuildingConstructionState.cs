using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingConstructionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ConstructionCompletesAtTick",
                table: "Buildings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConstructionCost",
                table: "Buildings",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnderConstruction",
                table: "Buildings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConstructionCompletesAtTick",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "ConstructionCost",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "IsUnderConstruction",
                table: "Buildings");
        }
    }
}
