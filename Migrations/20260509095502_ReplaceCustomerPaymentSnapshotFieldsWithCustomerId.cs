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
            MigrationMySqlCompat.DropForeignKeyIfExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_CustomerFuelGivens_CustomerFuelGivenId");

            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_Customer_Date");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_CustomerFuelGivenId");
            MigrationMySqlCompat.DropIndexIfExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_CustomerId");

            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "CustomerFuelGivenId",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `CustomerFuelGivenId`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "CustomerName",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `CustomerName`");
            MigrationMySqlCompat.RunIfColumnExists(migrationBuilder, "CustomerPayments", "CustomerPhone",
                "ALTER TABLE `CustomerPayments` DROP COLUMN `CustomerPhone`");

            MigrationMySqlCompat.AddColumnIfNotExists(migrationBuilder, "CustomerPayments", "CustomerId",
                "ALTER TABLE `CustomerPayments` ADD COLUMN `CustomerId` INT NOT NULL DEFAULT 0");

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

            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_Customer_Date",
                "CREATE INDEX `IX_CustomerPayments_Customer_Date` ON `CustomerPayments` (`BusinessId`, `CustomerId`, `PaymentDate`)");
            MigrationMySqlCompat.CreateIndexIfNotExists(migrationBuilder, "CustomerPayments", "IX_CustomerPayments_CustomerId",
                "CREATE INDEX `IX_CustomerPayments_CustomerId` ON `CustomerPayments` (`CustomerId`)");

            MigrationMySqlCompat.AddForeignKeyIfNotExists(migrationBuilder, "CustomerPayments", "FK_CustomerPayments_Customers_CustomerId",
                "ALTER TABLE `CustomerPayments` ADD CONSTRAINT `FK_CustomerPayments_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers`(`Id`) ON DELETE RESTRICT");
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
