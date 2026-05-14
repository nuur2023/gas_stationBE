using gas_station.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations;

[DbContext(typeof(GasStationDBContext))]
[Migration("20260514082000_RemoveAccountingPeriodCloseJournalEntryId")]
public partial class RemoveAccountingPeriodCloseJournalEntryId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CloseJournalEntryId",
            table: "AccountingPeriods");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CloseJournalEntryId",
            table: "AccountingPeriods",
            type: "int",
            nullable: true);
    }
}
