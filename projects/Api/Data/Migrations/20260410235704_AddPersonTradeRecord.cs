using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonTradeRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonTradeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    ShareCount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PricePerShare = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RecordedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonTradeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonTradeRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonTradeRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_PlayerId",
                table: "ChatMessages",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SentAtUtc",
                table: "ChatMessages",
                column: "SentAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PersonTradeRecords_CompanyId",
                table: "PersonTradeRecords",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonTradeRecords_PlayerId",
                table: "PersonTradeRecords",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonTradeRecords_RecordedAtTick",
                table: "PersonTradeRecords",
                column: "RecordedAtTick");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "PersonTradeRecords");
        }
    }
}
