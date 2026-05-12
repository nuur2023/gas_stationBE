using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCustomerPaymentSnapshotFieldsWithCustomerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                SET @fk_exists = (
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND CONSTRAINT_NAME = 'FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId'
                );
                SET @drop_fk_sql = IF(@fk_exists > 0,
                    'ALTER TABLE `CustomerPayments` DROP FOREIGN KEY `FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId`',
                    'SELECT 1');
                PREPARE stmt FROM @drop_fk_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
            ");

            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CustomerPayments_Customer_Date` ON `CustomerPayments`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CustomerPayments_CustomerFuelGivenId` ON `CustomerPayments`;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS `IX_CustomerPayments_CustomerId` ON `CustomerPayments`;");

            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `CustomerFuelGivenId`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `CustomerName`;");
            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` DROP COLUMN IF EXISTS `CustomerPhone`;");

            migrationBuilder.Sql("ALTER TABLE `CustomerPayments` ADD COLUMN IF NOT EXISTS `CustomerId` INT NOT NULL DEFAULT 0;");

            // Ensure every existing payment row has a valid customer id before creating FK.
            migrationBuilder.Sql(@"
                INSERT INTO `Customers` (`Name`, `Phone`, `StationId`, `BusinessId`, `CreatedAt`, `UpdatedAt`, `IsDeleted`)
                SELECT 'Unknown', '', 0, p.`BusinessId`, UTC_TIMESTAMP(), UTC_TIMESTAMP(), 0
                FROM `CustomerPayments` p
                LEFT JOIN `Customers` c
                    ON c.`BusinessId` = p.`BusinessId`
                   AND c.`Name` = 'Unknown'
                   AND c.`Phone` = ''
                   AND c.`IsDeleted` = 0
                WHERE p.`CustomerId` = 0 AND c.`Id` IS NULL
                GROUP BY p.`BusinessId`;
            ");
            migrationBuilder.Sql(@"
                UPDATE `CustomerPayments` p
                JOIN `Customers` c
                  ON c.`BusinessId` = p.`BusinessId`
                 AND c.`Name` = 'Unknown'
                 AND c.`Phone` = ''
                 AND c.`IsDeleted` = 0
                SET p.`CustomerId` = c.`Id`
                WHERE p.`CustomerId` = 0;
            ");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CustomerPayments_Customer_Date` ON `CustomerPayments` (`BusinessId`, `CustomerId`, `PaymentDate`);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS `IX_CustomerPayments_CustomerId` ON `CustomerPayments` (`CustomerId`);");

            migrationBuilder.Sql(@"
                SET @new_fk_exists = (
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                      AND CONSTRAINT_NAME = 'FK_CustomerPayments_Customers_CustomerId'
                );
                SET @add_fk_sql = IF(@new_fk_exists = 0,
                    'ALTER TABLE `CustomerPayments` ADD CONSTRAINT `FK_CustomerPayments_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers`(`Id`) ON DELETE RESTRICT',
                    'SELECT 1');
                PREPARE stmt2 FROM @add_fk_sql; EXECUTE stmt2; DEALLOCATE PREPARE stmt2;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerPayments_Customers_CustomerId",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_Customer_Date",
                table: "CustomerPayments");

            migrationBuilder.DropIndex(
                name: "IX_CustomerPayments_CustomerId",
                table: "CustomerPayments");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "CustomerPayments");

            migrationBuilder.AddColumn<int>(
                name: "CustomerFuelGivenId",
                table: "CustomerPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "CustomerPayments",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "CustomerPayments",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_Customer_Date",
                table: "CustomerPayments",
                columns: new[] { "BusinessId", "CustomerName", "CustomerPhone", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CustomerFuelGivenId",
                table: "CustomerPayments",
                column: "CustomerFuelGivenId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId",
                table: "CustomerPayments",
                column: "CustomerFuelGivenId",
                principalTable: "CustomerFuelGivens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
