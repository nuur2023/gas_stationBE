using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class CustomerLedgerAndCfgType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerFuelGivens", "Type",
                "ALTER TABLE `CustomerFuelGivens` ADD COLUMN `Type` VARCHAR(16) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Fuel'");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerFuelGivens", "CashAmount",
                "ALTER TABLE `CustomerFuelGivens` ADD COLUMN `CashAmount` DOUBLE NOT NULL DEFAULT 0");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerFuelGivens", "IX_CustomerFuelGivens_BusinessId_Date",
                "CREATE INDEX `IX_CustomerFuelGivens_BusinessId_Date` "
                + "ON `CustomerFuelGivens` (`BusinessId`, `Date`)");

            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` MODIFY COLUMN `CustomerFuelGivenId` INT NULL;");

            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "ChargedAmount",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `ChargedAmount` DOUBLE NOT NULL DEFAULT 0");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "Balance",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `Balance` DOUBLE NOT NULL DEFAULT 0");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "Description",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `Description` VARCHAR(32) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Payment'");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "ReferenceNo",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `ReferenceNo` VARCHAR(256) "
                + "CHARACTER SET utf8mb4 NULL");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "CustomerName",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `CustomerName` VARCHAR(256) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT ''");
            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "CustomerPhone",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `CustomerPhone` VARCHAR(64) "
                + "CHARACTER SET utf8mb4 NOT NULL DEFAULT ''");

            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_CustomerFuelGivenId",
                "CREATE INDEX `IX_CustomerPayments_CustomerFuelGivenId` "
                + "ON `CustomerPayments` (`CustomerFuelGivenId`)");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_UserId",
                "CREATE INDEX `IX_CustomerPayments_UserId` ON `CustomerPayments` (`UserId`)");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_BusinessId_PaymentDate",
                "CREATE INDEX `IX_CustomerPayments_BusinessId_PaymentDate` "
                + "ON `CustomerPayments` (`BusinessId`, `PaymentDate`)");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_Customer_Date",
                "CREATE INDEX `IX_CustomerPayments_Customer_Date` "
                + "ON `CustomerPayments` (`BusinessId`, `CustomerName`, `CustomerPhone`, `PaymentDate`)");

            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Businesses_BusinessId");
            MigrationMySqlCompat.AddForeignKeyIfNotExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Businesses_BusinessId",
                "ALTER TABLE `CustomerPayments` "
                + "ADD CONSTRAINT `FK_CustomerPayments_Businesses_BusinessId` "
                + "FOREIGN KEY (`BusinessId`) REFERENCES `Businesses` (`Id`) ON DELETE RESTRICT");

            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId");
            MigrationMySqlCompat.AddForeignKeyIfNotExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId",
                "ALTER TABLE `CustomerPayments` "
                + "ADD CONSTRAINT `FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId` "
                + "FOREIGN KEY (`CustomerFuelGivenId`) REFERENCES `CustomerFuelGivens` (`Id`) ON DELETE RESTRICT");

            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Users_UserId");
            MigrationMySqlCompat.AddForeignKeyIfNotExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Users_UserId",
                "ALTER TABLE `CustomerPayments` "
                + "ADD CONSTRAINT `FK_CustomerPayments_Users_UserId` "
                + "FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT");

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
            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Businesses_BusinessId");
            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId");
            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Users_UserId");

            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_Customer_Date");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_BusinessId_PaymentDate");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_UserId");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_CustomerFuelGivenId");

            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "CustomerPhone",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `CustomerPhone`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "CustomerName",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `CustomerName`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "ReferenceNo",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `ReferenceNo`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "Description",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `Description`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "Balance",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `Balance`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "ChargedAmount",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `ChargedAmount`");

            migrationBuilder.Sql(
                "ALTER TABLE `CustomerPayments` MODIFY COLUMN `CustomerFuelGivenId` INT NOT NULL;");

            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerFuelGivens", "IX_CustomerFuelGivens_BusinessId_Date");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerFuelGivens", "CashAmount",
                "ALTER TABLE `CustomerFuelGivens` DROP COLUMN `CashAmount`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerFuelGivens", "Type",
                "ALTER TABLE `CustomerFuelGivens` DROP COLUMN `Type`");
        }
    }
}
