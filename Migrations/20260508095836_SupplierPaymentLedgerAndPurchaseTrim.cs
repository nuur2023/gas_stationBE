using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPaymentLedgerAndPurchaseTrim : Migration
    {
        /// <summary>
        /// Uses INFORMATION_SCHEMA + prepared statements so DDL runs on MySQL / MariaDB
        /// builds that do not support <c>DROP COLUMN IF EXISTS</c>, <c>CHANGE COLUMN IF EXISTS</c>,
        /// <c>CREATE INDEX IF NOT EXISTS</c>, etc. (e.g. managed MySQL 8.0.x on DigitalOcean).
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purchases: drop legacy columns if present.
            ExecIfColumnExists(migrationBuilder, "Purchases", "AmountPaid",
                "ALTER TABLE `Purchases` DROP COLUMN `AmountPaid`");
            ExecIfColumnExists(migrationBuilder, "Purchases", "Status",
                "ALTER TABLE `Purchases` DROP COLUMN `Status`");

            // SupplierPayments: rename Amount → PaidAmount when Amount still exists.
            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "Amount",
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN `Amount` `PaidAmount` DOUBLE NOT NULL");

            AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "ChargedAmount",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `ChargedAmount` DOUBLE NOT NULL DEFAULT 0");
            AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "Balance",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `Balance` DOUBLE NOT NULL DEFAULT 0");
            AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "Description",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `Description` VARCHAR(32) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Payment'");
            AddColumnIfNotExists(migrationBuilder, "SupplierPayments", "PurchaseId",
                "ALTER TABLE `SupplierPayments` ADD COLUMN `PurchaseId` INT NULL");

            CreateIndexIfNotExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_BusinessId_SupplierId_Date",
                "CREATE INDEX `IX_SupplierPayments_BusinessId_SupplierId_Date` "
                + "ON `SupplierPayments` (`BusinessId`, `SupplierId`, `Date`)");
            CreateIndexIfNotExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_PurchaseId",
                "CREATE INDEX `IX_SupplierPayments_PurchaseId` ON `SupplierPayments` (`PurchaseId`)");

            DropForeignKeyIfExists(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId");

            ExecIfFkMissing(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId",
                "ALTER TABLE `SupplierPayments` "
                + "ADD CONSTRAINT `FK_SupplierPayments_Purchases_PurchaseId` "
                + "FOREIGN KEY (`PurchaseId`) REFERENCES `Purchases` (`Id`) ON DELETE RESTRICT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropForeignKeyIfExists(migrationBuilder, "SupplierPayments", "FK_SupplierPayments_Purchases_PurchaseId");

            DropIndexIfExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_PurchaseId");
            DropIndexIfExists(migrationBuilder, "SupplierPayments", "IX_SupplierPayments_BusinessId_SupplierId_Date");

            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "PurchaseId",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `PurchaseId`");
            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "Description",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `Description`");
            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "Balance",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `Balance`");
            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "ChargedAmount",
                "ALTER TABLE `SupplierPayments` DROP COLUMN `ChargedAmount`");

            ExecIfColumnExists(migrationBuilder, "SupplierPayments", "PaidAmount",
                "ALTER TABLE `SupplierPayments` CHANGE COLUMN `PaidAmount` `Amount` DOUBLE NOT NULL");

            AddColumnIfNotExists(migrationBuilder, "Purchases", "Status",
                "ALTER TABLE `Purchases` ADD COLUMN `Status` LONGTEXT "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT ''");
            AddColumnIfNotExists(migrationBuilder, "Purchases", "AmountPaid",
                "ALTER TABLE `Purchases` ADD COLUMN `AmountPaid` DOUBLE NOT NULL DEFAULT 0");
        }

        private static void ExecIfColumnExists(MigrationBuilder mb, string table, string column, string alterSql)
        {
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND COLUMN_NAME = '" + column + "') > 0, "
                + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        private static void AddColumnIfNotExists(MigrationBuilder mb, string table, string column, string alterSql)
        {
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND COLUMN_NAME = '" + column + "') = 0, "
                + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        private static void CreateIndexIfNotExists(MigrationBuilder mb, string table, string indexName, string createSql)
        {
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND INDEX_NAME = '" + indexName + "') = 0, "
                + "'" + EscapeSqlLiteral(createSql) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        private static void DropIndexIfExists(MigrationBuilder mb, string table, string indexName)
        {
            var alter = "ALTER TABLE `" + table + "` DROP INDEX `" + indexName + "`";
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND INDEX_NAME = '" + indexName + "') > 0, "
                + "'" + EscapeSqlLiteral(alter) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        private static void DropForeignKeyIfExists(MigrationBuilder mb, string table, string constraintName)
        {
            var alter = "ALTER TABLE `" + table + "` DROP FOREIGN KEY `" + constraintName + "`";
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
                + "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND CONSTRAINT_NAME = '" + constraintName + "' AND CONSTRAINT_TYPE = 'FOREIGN KEY') > 0, "
                + "'" + EscapeSqlLiteral(alter) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        private static void ExecIfFkMissing(MigrationBuilder mb, string table, string constraintName, string alterSql)
        {
            mb.Sql(
                "SET @__gs_exec := (SELECT IF("
                + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
                + "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + table + "' AND CONSTRAINT_NAME = '" + constraintName + "' AND CONSTRAINT_TYPE = 'FOREIGN KEY') = 0, "
                + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
                + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
        }

        /// <summary>Escape single quotes for embedding inside a single-quoted SQL string literal.</summary>
        private static string EscapeSqlLiteral(string sql) => sql.Replace("'", "''");
    }
}
