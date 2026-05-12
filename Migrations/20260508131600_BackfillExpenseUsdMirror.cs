using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class BackfillExpenseUsdMirror : Migration
    {
        /// <summary>
        /// Pre-existing USD expenses (Currency = 'USD') were saved with AmountUsd = 0 because the
        /// frontend zeroed it out for non-exchange entries. The "local amount" already IS the USD
        /// amount in those rows, so mirror it into AmountUsd (and clear Rate) so reports / cash-out
        /// totals add up correctly. New writes are normalised in the controller.
        /// Idempotent: safe to re-run because the WHERE clause filters out already-fixed rows.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE `Expenses`
                SET `AmountUsd` = `LocalAmount`,
                    `Rate` = 0
                WHERE `IsDeleted` = 0
                  AND UPPER(TRIM(`CurrencyCode`)) = 'USD'
                  AND `AmountUsd` = 0
                  AND `LocalAmount` <> 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill — no-op rollback. We can't restore the original 0 values without
            // losing legitimate data, so leave the corrected amounts in place.
        }
    }
}
