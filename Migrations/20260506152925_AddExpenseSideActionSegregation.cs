using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseSideActionSegregation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SideAction",
                table: "Expenses",
                type: "longtext",
                nullable: false,
                defaultValue: "Operation")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE `Expenses` SET `SideAction` = 'Operation' WHERE `SideAction` IS NULL OR `SideAction` = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SideAction",
                table: "Expenses");
        }
    }
}
