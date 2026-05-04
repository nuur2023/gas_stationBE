using gas_station.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations;

[DbContext(typeof(GasStationDBContext))]
[Migration("20260503190000_RemoveRetainedEarningsAccountId")]
public class RemoveRetainedEarningsAccountId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RetainedEarningsAccountId",
            table: "Businesses");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RetainedEarningsAccountId",
            table: "Businesses",
            type: "int",
            nullable: true);
    }
}
