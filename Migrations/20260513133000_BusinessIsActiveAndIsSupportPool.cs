using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations;

/// <inheritdoc />
public partial class BusinessIsActiveAndIsSupportPool : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Businesses",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsSupportPool",
            table: "Businesses",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsSupportPool",
            table: "Businesses");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Businesses");
    }
}
