using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPaymentLedgerAndPurchaseTrim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "Purchases", "AmountPaid",
                "ALTER TABLE `Purchases` DROP COLUMN `AmountPaid`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "Purchases", "Status",
                "ALTER TABLE `Purchases` DROP COLUMN `Status`");

            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "Amount",
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN `Amount` `PaidAmount` DOUBLE NOT NULL");

            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "ChargedAmount",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `ChargedAmount` DOUBLE NOT NULL DEFAULT 0");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "Balance",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `Balance` DOUBLE NOT NULL DEFAULT 0");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "Description",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `Description` VARCHAR(32) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Payment'");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "PurchaseId",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `PurchaseId` INT NULL");

            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_BusinessId_SupplierId_Date",
                "CREATE INDEX `IX_SupplierPayments_BusinessId_SupplierId_Date` "
                + "ON `SupplierPayments` (`BusinessId`, `SupplierId`, `Date`)");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_PurchaseId",
                "CREATE INDEX `IX_SupplierPayments_PurchaseId` ON `SupplierPayments` (`PurchaseId`)");

            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId");

            MigrationMySqlCompat.AddForeignKeyIfNotExists(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId",
                "ALTER TABLE `SupplierPayments` "
                + "ADD CONSTRAINT `FK_SupplierPayments_Purchases_PurchaseId` "
                + "FOREIGN KEY (`PurchaseId`) REFERENCES `Purchases` (`Id`) ON DELETE RESTRICT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId");

            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_PurchaseId");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_BusinessId_SupplierId_Date");

            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "PurchaseId",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `PurchaseId`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "Description",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `Description`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "Balance",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `Balance`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "ChargedAmount",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `ChargedAmount`");

            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "SupplierPayments", "PaidAmount",
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN `PaidAmount` `Amount` DOUBLE NOT NULL");

            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "Purchases", "Status",
                "ALTER TABLE `Purchases` ADD COLUMN `Status` LONGTEXT "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT ''");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "Purchases", "AmountPaid",
                "ALTER TABLE `Purchases` ADD COLUMN `AmountPaid` DOUBLE NOT NULL DEFAULT 0");
        }
    }
}
