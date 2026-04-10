using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketTrendState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StartupPackOffers");

            migrationBuilder.AddColumn<decimal>(
                name: "TrendFactor",
                table: "PublicSalesRecords",
                type: "TEXT",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "MarketTrendStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrendFactor = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    LastUpdatedTick = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketTrendStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketTrendStates_CityId_ItemId",
                table: "MarketTrendStates",
                columns: new[] { "CityId", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketTrendStates");

            migrationBuilder.DropColumn(
                name: "TrendFactor",
                table: "PublicSalesRecords");

            migrationBuilder.CreateTable(
                name: "StartupPackOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompanyCashGrant = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DismissedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantedCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OfferKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProDurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ShownAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StartupPackOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StartupPackOffers_Companies_GrantedCompanyId",
                        column: x => x.GrantedCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StartupPackOffers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StartupPackOffers_GrantedCompanyId",
                table: "StartupPackOffers",
                column: "GrantedCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StartupPackOffers_PlayerId",
                table: "StartupPackOffers",
                column: "PlayerId",
                unique: true);
        }
    }
}
