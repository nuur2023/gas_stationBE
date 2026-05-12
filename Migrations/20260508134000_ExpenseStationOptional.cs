using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseStationOptional : Migration
    {
        /// <summary>
        /// Management-side expenses are recorded at the business level and have no station context,
        /// so the StationId column becomes nullable. As a one-shot data fix any existing
        /// SideAction='Management' rows with a non-zero StationId are reset to NULL so historical
        /// data matches the new constraint.
        /// Idempotent: re-running the MODIFY is a no-op once the column is already nullable, and
        /// the UPDATE filters out rows that have already been cleared.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `Expenses` MODIFY COLUMN `StationId` INT NULL;"
            );

            migrationBuilder.Sql(@"
                UPDATE `Expenses`
                SET `StationId` = NULL
                WHERE `IsDeleted` = 0
                  AND `SideAction` = 'Management'
                  AND `StationId` IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reliably restore the original StationId values that were nulled above,
            // so we only restore the NOT NULL constraint with a 0 default for safety.
            migrationBuilder.Sql(@"
                UPDATE `Expenses`
                SET `StationId` = 0
                WHERE `StationId` IS NULL;
            ");
            migrationBuilder.Sql(
                "ALTER TABLE `Expenses` MODIFY COLUMN `StationId` INT NOT NULL DEFAULT 0;"
            );
        }
    }
}
