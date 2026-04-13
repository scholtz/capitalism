using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId_Category_RecordedAtTick",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "Category", "RecordedAtTick" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_CompanyId_Category_RecordedAtTick",
                table: "LedgerEntries");
        }
    }
}
