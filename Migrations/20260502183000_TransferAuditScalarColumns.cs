using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class TransferAuditScalarColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessId",
                table: "TransferInventoryAudits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToStationId",
                table: "TransferInventoryAudits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Liters",
                table: "TransferInventoryAudits",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "TransferInventoryAudits",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE `TransferInventoryAudits` AS a
                INNER JOIN `TransferInventories` AS t ON t.`Id` = a.`TransferInventoryId`
                INNER JOIN `BusinessFuelInventories` AS b ON b.`Id` = t.`BusinessFuelInventoryId` AND b.`IsDeleted` = 0
                SET a.`BusinessId` = b.`BusinessId`, a.`ToStationId` = t.`ToStationId`, a.`Liters` = t.`Liters`, a.`Date` = t.`Date`;
                """);

            migrationBuilder.Sql("DELETE FROM `TransferInventoryAudits` WHERE `BusinessId` IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "BusinessId",
                table: "TransferInventoryAudits",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ToStationId",
                table: "TransferInventoryAudits",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Liters",
                table: "TransferInventoryAudits",
                type: "double",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "TransferInventoryAudits",
                type: "datetime(6)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "BeforeJson",
                table: "TransferInventoryAudits");

            migrationBuilder.DropColumn(
                name: "AfterJson",
                table: "TransferInventoryAudits");

            migrationBuilder.CreateIndex(
                name: "IX_TransferInventoryAudits_BusinessId",
                table: "TransferInventoryAudits",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferInventoryAudits_ToStationId",
                table: "TransferInventoryAudits",
                column: "ToStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransferInventoryAudits_Businesses_BusinessId",
                table: "TransferInventoryAudits",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TransferInventoryAudits_Stations_ToStationId",
                table: "TransferInventoryAudits",
                column: "ToStationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransferInventoryAudits_Stations_ToStationId",
                table: "TransferInventoryAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_TransferInventoryAudits_Businesses_BusinessId",
                table: "TransferInventoryAudits");

            migrationBuilder.DropIndex(
                name: "IX_TransferInventoryAudits_ToStationId",
                table: "TransferInventoryAudits");

            migrationBuilder.DropIndex(
                name: "IX_TransferInventoryAudits_BusinessId",
                table: "TransferInventoryAudits");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "TransferInventoryAudits");

            migrationBuilder.DropColumn(
                name: "Liters",
                table: "TransferInventoryAudits");

            migrationBuilder.DropColumn(
                name: "ToStationId",
                table: "TransferInventoryAudits");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "TransferInventoryAudits");

            migrationBuilder.AddColumn<string>(
                name: "AfterJson",
                table: "TransferInventoryAudits",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BeforeJson",
                table: "TransferInventoryAudits",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
