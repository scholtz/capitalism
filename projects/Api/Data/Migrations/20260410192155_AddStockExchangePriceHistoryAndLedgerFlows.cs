using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockExchangePriceHistoryAndLedgerFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInvisibleInChat",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AdminActionAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdminActorPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdminActorEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AdminActorDisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EffectivePlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectivePlayerEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EffectivePlayerDisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EffectiveAccountType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EffectiveCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EffectiveCompanyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    GraphQlOperationName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    MutationSummary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActionAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharePriceHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SharePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RecordedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharePriceHistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharePriceHistoryEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionAuditLogs_AdminActorPlayerId",
                table: "AdminActionAuditLogs",
                column: "AdminActorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionAuditLogs_EffectivePlayerId",
                table: "AdminActionAuditLogs",
                column: "EffectivePlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionAuditLogs_RecordedAtUtc",
                table: "AdminActionAuditLogs",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SharePriceHistoryEntries_CompanyId_RecordedAtTick_RecordedAtUtc",
                table: "SharePriceHistoryEntries",
                columns: new[] { "CompanyId", "RecordedAtTick", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminActionAuditLogs");

            migrationBuilder.DropTable(
                name: "SharePriceHistoryEntries");

            migrationBuilder.DropColumn(
                name: "IsInvisibleInChat",
                table: "Players");
        }
    }
}
