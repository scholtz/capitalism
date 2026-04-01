using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingPendingRentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PendingPriceActivationTick",
                table: "Buildings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingPricePerSqm",
                table: "Buildings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingPriceActivationTick",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "PendingPricePerSqm",
                table: "Buildings");
        }
    }
}
