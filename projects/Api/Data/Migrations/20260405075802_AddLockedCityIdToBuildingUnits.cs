using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLockedCityIdToBuildingUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LockedCityId",
                table: "BuildingUnits",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LockedCityId",
                table: "BuildingConfigurationPlanUnits",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedCityId",
                table: "BuildingUnits");

            migrationBuilder.DropColumn(
                name: "LockedCityId",
                table: "BuildingConfigurationPlanUnits");
        }
    }
}
