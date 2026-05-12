using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class CustomerLedgerAndCfgType : Migration
    {
        /// <summary>
        /// Adds Type / CashAmount to CustomerFuelGivens and extends CustomerPayments into a
        /// supplier-style ledger (Charged / Payment description, ChargedAmount, Balance,
        /// ReferenceNo, denormalised CustomerName + CustomerPhone, optional CustomerFuelGivenId).
        /// Idempotent SQL so a partially-applied retry is safe.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CustomerFuelGivens — Type + CashAmount + index.
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerFuelGivens` ADD COLUMN IF NOT EXISTS `Type` VARCHAR(16) "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Fuel';"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerFuelGivens` ADD COLUMN IF NOT EXISTS `CashAmount` DOUBLE NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_CustomerFuelGivens_BusinessId_Date` "
                    + "ON `CustomerFuelGivens` (`BusinessId`, `Date`);"
            );

            // CustomerPayments — extend into ledger.
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` MODIFY COLUMN `CustomerFuelGivenId` INT NULL;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `ChargedAmount` DOUBLE NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `Balance` DOUBLE NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `Description` VARCHAR(32) "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Payment';"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `ReferenceNo` VARCHAR(256) "
                    + "CHARACTER SET utf8mb4 NULL;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `CustomerName` VARCHAR(256) "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT '';"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `CustomerPhone` VARCHAR(64) "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT '';"
            );

            // Indexes.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_CustomerPayments_CustomerFuelGivenId` "
                    + "ON `CustomerPayments` (`CustomerFuelGivenId`);"
            );
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_CustomerPayments_UserId` "
                    + "ON `CustomerPayments` (`UserId`);"
            );
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_CustomerPayments_BusinessId_PaymentDate` "
                    + "ON `CustomerPayments` (`BusinessId`, `PaymentDate`);"
            );
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS "
                    + "`IX_CustomerPayments_Customer_Date` "
                    + "ON `CustomerPayments` (`BusinessId`, `CustomerName`, `CustomerPhone`, `PaymentDate`);"
            );

            // Foreign keys (drop-then-add so MariaDB without "ADD CONSTRAINT IF NOT EXISTS" works).
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_Businesses_BusinessId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "ADD CONSTRAINT `FK_CustomerPayments_Businesses_BusinessId` "
                    + "FOREIGN KEY (`BusinessId`) REFERENCES `Businesses` (`Id`) ON DELETE RESTRICT;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "ADD CONSTRAINT `FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId` "
                    + "FOREIGN KEY (`CustomerFuelGivenId`) REFERENCES `CustomerFuelGivens` (`Id`) ON DELETE RESTRICT;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_Users_UserId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` "
                    + "ADD CONSTRAINT `FK_CustomerPayments_Users_UserId` "
                    + "FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT;"
            );

            // Backfill: any existing AmountPaid > 0 rows are real payments. Fill Description, names
            // from the linked CustomerFuelGiven, and recompute Balance (best effort: per-customer
            // chronological running balance is recalculated on the next save in code as well).
            migrationBuilder.Sql(@"
                UPDATE `CustomerPayments` cp
                JOIN `CustomerFuelGivens` cfg ON cfg.`Id` = cp.`CustomerFuelGivenId`
                SET cp.`CustomerName` = cfg.`Name`,
                    cp.`CustomerPhone` = cfg.`Phone`,
                    cp.`Description` = 'Payment'
                WHERE cp.`CustomerFuelGivenId` IS NOT NULL AND cp.`CustomerName` = '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_Businesses_BusinessId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP FOREIGN KEY IF EXISTS `FK_CustomerPayments_Users_UserId`;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP INDEX IF EXISTS `IX_CustomerPayments_Customer_Date`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP INDEX IF EXISTS `IX_CustomerPayments_BusinessId_PaymentDate`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP INDEX IF EXISTS `IX_CustomerPayments_UserId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` DROP INDEX IF EXISTS `IX_CustomerPayments_CustomerFuelGivenId`;"
            );

            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `CustomerPhone`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `CustomerName`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `ReferenceNo`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `Description`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `Balance`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `ChargedAmount`;");
            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` MODIFY COLUMN `CustomerFuelGivenId` INT NOT NULL;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE `CustomerFuelGivens` DROP INDEX IF EXISTS `IX_CustomerFuelGivens_BusinessId_Date`;"
            );
            migrationBuilder.Sql("ALTER TABLE `CustomerFuelGivens` DROP COLUMN IF EXISTS `CashAmount`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerFuelGivens` DROP COLUMN IF EXISTS `Type`;");
        }
    }
}
