using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPaymentLedgerAndPurchaseTrim : Migration
    {
        /// <summary>
        /// Idempotent migration that uses MariaDB / MySQL <c>IF [NOT] EXISTS</c> on every DDL
        /// operation. This lets us rerun safely after a failed prior attempt (the original
        /// version used <c>RENAME COLUMN</c>, which is unsupported on MariaDB &lt; 10.5.2).
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purchases: drop legacy fields (Status / AmountPaid). May already be dropped.
            migrationBuilder.Sql("ALTER TABLE `Purchases` DROP COLUMN IF EXISTS `AmountPaid`;");
            migrationBuilder.Sql("ALTER TABLE `Purchases` DROP COLUMN IF EXISTS `Status`;");

            // SupplierPayments: rename Amount → PaidAmount via CHANGE COLUMN (MariaDB 10.0.2+).
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN IF EXISTS `Amount` `PaidAmount` DOUBLE NOT NULL;"
            );

            // New ledger columns (idempotent).
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` ADD COLUMN IF NOT EXISTS `ChargedAmount` DOUBLE NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` ADD COLUMN IF NOT EXISTS `Balance` DOUBLE NOT NULL DEFAULT 0;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` ADD COLUMN IF NOT EXISTS `Description` VARCHAR(32) "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Payment';"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` ADD COLUMN IF NOT EXISTS `PurchaseId` INT NULL;"
            );

            // Indexes (idempotent).
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_SupplierPayments_BusinessId_SupplierId_Date` "
                    + "ON `SupplierPayments` (`BusinessId`, `SupplierId`, `Date`);"
            );
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS `IX_SupplierPayments_PurchaseId` "
                    + "ON `SupplierPayments` (`PurchaseId`);"
            );

            // FK SupplierPayments.PurchaseId → Purchases.Id.
            // Older MariaDB does not accept `ADD CONSTRAINT IF NOT EXISTS … FOREIGN KEY`,
            // so drop-then-add (DROP FK IF EXISTS works on MariaDB 10.0.2+).
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` DROP FOREIGN KEY IF EXISTS `FK_SupplierPayments_Purchases_PurchaseId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` "
                    + "ADD CONSTRAINT `FK_SupplierPayments_Purchases_PurchaseId` "
                    + "FOREIGN KEY (`PurchaseId`) REFERENCES `Purchases` (`Id`) ON DELETE RESTRICT;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` DROP FOREIGN KEY IF EXISTS `FK_SupplierPayments_Purchases_PurchaseId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` DROP INDEX IF EXISTS `IX_SupplierPayments_PurchaseId`;"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` DROP INDEX IF EXISTS `IX_SupplierPayments_BusinessId_SupplierId_Date`;"
            );

            migrationBuilder.Sql("ALTER TABLE `SupplierPayments` DROP COLUMN IF EXISTS `PurchaseId`;");
            migrationBuilder.Sql("ALTER TABLE `SupplierPayments` DROP COLUMN IF EXISTS `Description`;");
            migrationBuilder.Sql("ALTER TABLE `SupplierPayments` DROP COLUMN IF EXISTS `Balance`;");
            migrationBuilder.Sql("ALTER TABLE `SupplierPayments` DROP COLUMN IF EXISTS `ChargedAmount`;");

            migrationBuilder.Sql(
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN IF EXISTS `PaidAmount` `Amount` DOUBLE NOT NULL;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE `Purchases` ADD COLUMN IF NOT EXISTS `Status` LONGTEXT "
                    + "CHARACTER SET utf8mb4 NOT NULL DEFAULT '';"
            );
            migrationBuilder.Sql(
                "ALTER TABLE `Purchases` ADD COLUMN IF NOT EXISTS `AmountPaid` DOUBLE NOT NULL DEFAULT 0;"
            );
        }
    }
}
