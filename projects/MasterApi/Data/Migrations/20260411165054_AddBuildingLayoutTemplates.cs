using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingLayoutTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildingLayoutTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BuildingType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UnitsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingLayoutTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingLayoutTemplates_PlayerAccounts_PlayerAccountId",
                        column: x => x.PlayerAccountId,
                        principalTable: "PlayerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingLayoutTemplates_PlayerAccountId_BuildingType",
                table: "BuildingLayoutTemplates",
                columns: new[] { "PlayerAccountId", "BuildingType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildingLayoutTemplates");
        }
    }
}
