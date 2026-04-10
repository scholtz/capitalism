using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CS8981 // Generated migration class name 'init' is lowercase; required by EF Core migration tooling.

namespace MasterApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameNewsEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetServerKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedByEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedByEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNewsEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Environment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BackendUrl = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    GraphqlUrl = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    FrontendUrl = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PlayerCount = table.Column<int>(type: "integer", nullable: false),
                    CompanyCount = table.Column<int>(type: "integer", nullable: false),
                    CurrentTick = table.Column<long>(type: "bigint", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalGameAdminGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GrantedByEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalGameAdminGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartupPackClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameNewsEntryLocalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameNewsEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    HtmlContent = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNewsEntryLocalizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameNewsEntryLocalizations_GameNewsEntries_GameNewsEntryId",
                        column: x => x.GameNewsEntryId,
                        principalTable: "GameNewsEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameNewsReadReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameNewsEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServerKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNewsReadReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameNewsReadReceipts_GameNewsEntries_GameNewsEntryId",
                        column: x => x.GameNewsEntryId,
                        principalTable: "GameNewsEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProSubscriptions_PlayerAccounts_PlayerAccountId",
                        column: x => x.PlayerAccountId,
                        principalTable: "PlayerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameNewsEntries_TargetServerKey_PublishedAtUtc",
                table: "GameNewsEntries",
                columns: new[] { "TargetServerKey", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameNewsEntryLocalizations_GameNewsEntryId_Locale",
                table: "GameNewsEntryLocalizations",
                columns: new[] { "GameNewsEntryId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameNewsReadReceipts_GameNewsEntryId_PlayerEmail_ServerKey",
                table: "GameNewsReadReceipts",
                columns: new[] { "GameNewsEntryId", "PlayerEmail", "ServerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameServers_ServerKey",
                table: "GameServers",
                column: "ServerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalGameAdminGrants_Email",
                table: "GlobalGameAdminGrants",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAccounts_Email",
                table: "PlayerAccounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProSubscriptions_PlayerAccountId",
                table: "ProSubscriptions",
                column: "PlayerAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameNewsEntryLocalizations");

            migrationBuilder.DropTable(
                name: "GameNewsReadReceipts");

            migrationBuilder.DropTable(
                name: "GameServers");

            migrationBuilder.DropTable(
                name: "GlobalGameAdminGrants");

            migrationBuilder.DropTable(
                name: "ProSubscriptions");

            migrationBuilder.DropTable(
                name: "GameNewsEntries");

            migrationBuilder.DropTable(
                name: "PlayerAccounts");
        }
    }
}
