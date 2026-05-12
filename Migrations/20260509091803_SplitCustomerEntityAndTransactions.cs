using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class SplitCustomerEntityAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS `Customers` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
                    `Phone` varchar(64) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
                    `StationId` int NOT NULL,
                    `BusinessId` int NOT NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    `UpdatedAt` datetime(6) NOT NULL,
                    `IsDeleted` tinyint(1) NOT NULL,
                    CONSTRAINT `PK_Customers` PRIMARY KEY (`Id`)
                ) CHARACTER SET=utf8mb4;
                """);

            migrationBuilder.Sql(
                """
                SET @addCustomerId := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND COLUMN_NAME = 'CustomerId'
                    ),
                    'SELECT 1',
                    'ALTER TABLE `CustomerFuelGivens` ADD `CustomerId` int NOT NULL DEFAULT 0'
                );
                PREPARE stmt FROM @addCustomerId;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO `Customers` (`Name`, `Phone`, `StationId`, `BusinessId`, `CreatedAt`, `UpdatedAt`, `IsDeleted`)
                SELECT src.`Name`, src.`Phone`, src.`StationId`, src.`BusinessId`, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                FROM (
                    SELECT DISTINCT
                        COALESCE(NULLIF(TRIM(p.`CustomerName`), ''), 'Unknown') AS `Name`,
                        COALESCE(TRIM(p.`CustomerPhone`), '') AS `Phone`,
                        g.`StationId`,
                        g.`BusinessId`
                    FROM `CustomerFuelGivens` g
                    LEFT JOIN `CustomerPayments` p
                        ON p.`CustomerFuelGivenId` = g.`Id`
                       AND p.`IsDeleted` = 0
                    WHERE 1 = 1
                ) src
                LEFT JOIN `Customers` c
                    ON c.`BusinessId` = src.`BusinessId`
                   AND c.`StationId` = src.`StationId`
                   AND c.`Name` = src.`Name`
                   AND c.`Phone` = src.`Phone`
                   AND c.`IsDeleted` = 0
                WHERE c.`Id` IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE `CustomerFuelGivens` g
                LEFT JOIN `CustomerPayments` p
                    ON p.`CustomerFuelGivenId` = g.`Id`
                   AND p.`IsDeleted` = 0
                LEFT JOIN `Customers` c
                    ON c.`BusinessId` = g.`BusinessId`
                   AND c.`StationId` = g.`StationId`
                   AND c.`Name` = COALESCE(NULLIF(TRIM(p.`CustomerName`), ''), 'Unknown')
                   AND c.`Phone` = COALESCE(TRIM(p.`CustomerPhone`), '')
                   AND c.`IsDeleted` = 0
                SET g.`CustomerId` = COALESCE(c.`Id`, g.`CustomerId`)
                WHERE (g.`CustomerId` IS NULL OR g.`CustomerId` = 0);
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO `Customers` (`Name`, `Phone`, `StationId`, `BusinessId`, `CreatedAt`, `UpdatedAt`, `IsDeleted`)
                SELECT 'Unknown', '', src.`StationId`, src.`BusinessId`, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                FROM (
                    SELECT DISTINCT g.`StationId`, g.`BusinessId`
                    FROM `CustomerFuelGivens` g
                    WHERE (g.`CustomerId` IS NULL OR g.`CustomerId` = 0)
                ) src
                LEFT JOIN `Customers` c
                    ON c.`BusinessId` = src.`BusinessId`
                   AND c.`StationId` = src.`StationId`
                   AND c.`Name` = 'Unknown'
                   AND c.`Phone` = ''
                   AND c.`IsDeleted` = 0
                WHERE c.`Id` IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE `CustomerFuelGivens` g
                JOIN `Customers` c
                    ON c.`BusinessId` = g.`BusinessId`
                   AND c.`StationId` = g.`StationId`
                   AND c.`Name` = 'Unknown'
                   AND c.`Phone` = ''
                   AND c.`IsDeleted` = 0
                SET g.`CustomerId` = c.`Id`
                WHERE (g.`CustomerId` IS NULL OR g.`CustomerId` = 0);
                """);

            migrationBuilder.Sql(
                """
                SET @dropName := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND COLUMN_NAME = 'Name'
                    ),
                    'ALTER TABLE `CustomerFuelGivens` DROP COLUMN `Name`',
                    'SELECT 1'
                );
                PREPARE stmt FROM @dropName;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @dropPhone := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND COLUMN_NAME = 'Phone'
                    ),
                    'ALTER TABLE `CustomerFuelGivens` DROP COLUMN `Phone`',
                    'SELECT 1'
                );
                PREPARE stmt FROM @dropPhone;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @idx1 := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND INDEX_NAME = 'IX_CustomerFuelGivens_BusinessId_CustomerId_Date'
                    ),
                    'SELECT 1',
                    'CREATE INDEX `IX_CustomerFuelGivens_BusinessId_CustomerId_Date` ON `CustomerFuelGivens` (`BusinessId`, `CustomerId`, `Date`)'
                );
                PREPARE stmt FROM @idx1;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @idx2 := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND INDEX_NAME = 'IX_CustomerFuelGivens_CustomerId'
                    ),
                    'SELECT 1',
                    'CREATE INDEX `IX_CustomerFuelGivens_CustomerId` ON `CustomerFuelGivens` (`CustomerId`)'
                );
                PREPARE stmt FROM @idx2;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @idx3 := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'Customers'
                          AND INDEX_NAME = 'IX_Customers_BusinessId_Name_Phone'
                    ),
                    'SELECT 1',
                    'CREATE INDEX `IX_Customers_BusinessId_Name_Phone` ON `Customers` (`BusinessId`, `Name`, `Phone`)'
                );
                PREPARE stmt FROM @idx3;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @idx4 := IF(
                    EXISTS(
                        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'Customers'
                          AND INDEX_NAME = 'IX_Customers_BusinessId_StationId'
                    ),
                    'SELECT 1',
                    'CREATE INDEX `IX_Customers_BusinessId_StationId` ON `Customers` (`BusinessId`, `StationId`)'
                );
                PREPARE stmt FROM @idx4;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @addFk := IF(
                    EXISTS(
                        SELECT 1
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                        WHERE CONSTRAINT_SCHEMA = DATABASE()
                          AND TABLE_NAME = 'CustomerFuelGivens'
                          AND CONSTRAINT_NAME = 'FK_CustomerFuelGivens_Customers_CustomerId'
                    ),
                    'SELECT 1',
                    'ALTER TABLE `CustomerFuelGivens` ADD CONSTRAINT `FK_CustomerFuelGivens_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE RESTRICT'
                );
                PREPARE stmt FROM @addFk;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerFuelGivens_Customers_CustomerId",
                table: "CustomerFuelGivens");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerFuelGivens_BusinessId_CustomerId_Date",
                table: "CustomerFuelGivens");

            migrationBuilder.DropIndex(
                name: "IX_CustomerFuelGivens_CustomerId",
                table: "CustomerFuelGivens");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "CustomerFuelGivens");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CustomerFuelGivens",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "CustomerFuelGivens",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
