using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class RecurringJournalStationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StationId",
                table: "RecurringJournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_StationId",
                table: "RecurringJournalEntries",
                column: "StationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringJournalEntries_Stations_StationId",
                table: "RecurringJournalEntries",
                column: "StationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringJournalEntries_Stations_StationId",
                table: "RecurringJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_RecurringJournalEntries_StationId",
                table: "RecurringJournalEntries");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "RecurringJournalEntries");
        }
    }
}
