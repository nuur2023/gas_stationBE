using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseCurrencyIdCustomerFuelCurrencyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE `Expenses` e
INNER JOIN `Currencies` c ON UPPER(TRIM(c.`Code`)) = UPPER(TRIM(e.`CurrencyCode`)) AND c.`IsDeleted` = 0
SET e.`CurrencyId` = c.`Id`
WHERE e.`IsDeleted` = 0;");

            migrationBuilder.Sql(@"
UPDATE `Expenses` e
SET e.`CurrencyId` = (
  SELECT c.`Id` FROM `Currencies` c WHERE c.`Code` = 'USD' AND c.`IsDeleted` = 0 ORDER BY c.`Id` LIMIT 1
)
WHERE e.`CurrencyId` IS NULL;");

            migrationBuilder.Sql("ALTER TABLE `Expenses` MODIFY COLUMN `CurrencyId` int NOT NULL");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Expenses");

            migrationBuilder.AddColumn<int>(
                name: "CurrencyId",
                table: "CustomerFuelGivens",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE `CustomerFuelGivens` g
SET g.`CurrencyId` = (
  SELECT c.`Id` FROM `Currencies` c WHERE c.`Code` = 'SSP' AND c.`IsDeleted` = 0 ORDER BY c.`Id` LIMIT 1
)
WHERE g.`CurrencyId` IS NULL;");

            migrationBuilder.Sql(@"
UPDATE `CustomerFuelGivens` g
SET g.`CurrencyId` = (
  SELECT c.`Id` FROM `Currencies` c WHERE c.`IsDeleted` = 0 ORDER BY c.`Id` LIMIT 1
)
WHERE g.`CurrencyId` IS NULL;");

            migrationBuilder.Sql("ALTER TABLE `CustomerFuelGivens` MODIFY COLUMN `CurrencyId` int NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-lossy reverse: not supported for production rollback.
        }
    }
}
