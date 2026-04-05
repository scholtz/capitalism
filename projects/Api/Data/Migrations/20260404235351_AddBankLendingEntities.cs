using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankLendingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankBuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LenderCompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnnualInterestRatePercent = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    MaxPrincipalPerLoan = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalCapacity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UsedCapacity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DurationTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoanOffers_Buildings_BankBuildingId",
                        column: x => x.BankBuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoanOffers_Companies_LenderCompanyId",
                        column: x => x.LenderCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoanOfferId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BorrowerCompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankBuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LenderCompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalPrincipal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RemainingPrincipal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AnnualInterestRatePercent = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false),
                    DurationTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTick = table.Column<long>(type: "INTEGER", nullable: false),
                    DueTick = table.Column<long>(type: "INTEGER", nullable: false),
                    NextPaymentTick = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PaymentsMade = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPayments = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MissedPayments = table.Column<int>(type: "INTEGER", nullable: false),
                    AccumulatedPenalty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loans_Buildings_BankBuildingId",
                        column: x => x.BankBuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_Companies_BorrowerCompanyId",
                        column: x => x.BorrowerCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_Companies_LenderCompanyId",
                        column: x => x.LenderCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Loans_LoanOffers_LoanOfferId",
                        column: x => x.LoanOfferId,
                        principalTable: "LoanOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanOffers_BankBuildingId",
                table: "LoanOffers",
                column: "BankBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanOffers_LenderCompanyId_IsActive",
                table: "LoanOffers",
                columns: new[] { "LenderCompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_BankBuildingId",
                table: "Loans",
                column: "BankBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_BorrowerCompanyId_Status",
                table: "Loans",
                columns: new[] { "BorrowerCompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LenderCompanyId_Status",
                table: "Loans",
                columns: new[] { "LenderCompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LoanOfferId",
                table: "Loans",
                column: "LoanOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_NextPaymentTick",
                table: "Loans",
                column: "NextPaymentTick");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loans");

            migrationBuilder.DropTable(
                name: "LoanOffers");
        }
    }
}
