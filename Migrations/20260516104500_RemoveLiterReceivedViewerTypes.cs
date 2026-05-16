using System;
using gas_station.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations;

[DbContext(typeof(GasStationDBContext))]
[Migration("20260516104500_RemoveLiterReceivedViewerTypes")]
public partial class RemoveLiterReceivedViewerTypes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_LiterReceiveds_LiterReceivedViewerTypes_ViewerTypeId",
            table: "LiterReceiveds");

        migrationBuilder.DropIndex(
            name: "IX_LiterReceiveds_ViewerTypeId",
            table: "LiterReceiveds");

        migrationBuilder.DropColumn(
            name: "ViewerTypeId",
            table: "LiterReceiveds");

        migrationBuilder.DropTable(
            name: "LiterReceivedViewerTypes");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LiterReceivedViewerTypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Code = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LiterReceivedViewerTypes", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_LiterReceivedViewerTypes_Code",
            table: "LiterReceivedViewerTypes",
            column: "Code",
            unique: true);

        migrationBuilder.AddColumn<int>(
            name: "ViewerTypeId",
            table: "LiterReceiveds",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_LiterReceiveds_ViewerTypeId",
            table: "LiterReceiveds",
            column: "ViewerTypeId");

        migrationBuilder.AddForeignKey(
            name: "FK_LiterReceiveds_LiterReceivedViewerTypes_ViewerTypeId",
            table: "LiterReceiveds",
            column: "ViewerTypeId",
            principalTable: "LiterReceivedViewerTypes",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.Sql(
            "INSERT INTO LiterReceivedViewerTypes (Name, Code, CreatedAt, UpdatedAt, IsDeleted) " +
            "VALUES ('Our Turn Fare', 'our_turn_fare', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0);");
    }
}
