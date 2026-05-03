using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class AccountingRecurringAndPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "EntryKind",
                table: "JournalEntries",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "RecurringJournalEntryId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetainedEarningsAccountId",
                table: "Businesses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusinessId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    CloseJournalEntryId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingPeriods_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RecurringJournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BusinessId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DebitAccountId = table.Column<int>(type: "int", nullable: false),
                    CreditAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<double>(type: "double", nullable: false),
                    Frequency = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AutoPost = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPaused = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    CustomerFuelGivenId = table.Column<int>(type: "int", nullable: true),
                    LastRunDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextRunDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PostingUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringJournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_Accounts_CreditAccountId",
                        column: x => x.CreditAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_Accounts_DebitAccountId",
                        column: x => x.DebitAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_CustomerFuelGivens_CustomerFuelGiven~",
                        column: x => x.CustomerFuelGivenId,
                        principalTable: "CustomerFuelGivens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringJournalEntries_Users_PostingUserId",
                        column: x => x.PostingUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_RecurringJournalEntryId",
                table: "JournalEntries",
                column: "RecurringJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_RetainedEarningsAccountId",
                table: "Businesses",
                column: "RetainedEarningsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_BusinessId_PeriodStart_PeriodEnd",
                table: "AccountingPeriods",
                columns: new[] { "BusinessId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_BusinessId_NextRunDate",
                table: "RecurringJournalEntries",
                columns: new[] { "BusinessId", "NextRunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_CreditAccountId",
                table: "RecurringJournalEntries",
                column: "CreditAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_CustomerFuelGivenId",
                table: "RecurringJournalEntries",
                column: "CustomerFuelGivenId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_DebitAccountId",
                table: "RecurringJournalEntries",
                column: "DebitAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_PostingUserId",
                table: "RecurringJournalEntries",
                column: "PostingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringJournalEntries_SupplierId",
                table: "RecurringJournalEntries",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_Accounts_RetainedEarningsAccountId",
                table: "Businesses",
                column: "RetainedEarningsAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_RecurringJournalEntries_RecurringJournalEntry~",
                table: "JournalEntries",
                column: "RecurringJournalEntryId",
                principalTable: "RecurringJournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_Accounts_RetainedEarningsAccountId",
                table: "Businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_RecurringJournalEntries_RecurringJournalEntry~",
                table: "JournalEntries");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "RecurringJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_RecurringJournalEntryId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_RetainedEarningsAccountId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "EntryKind",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "RecurringJournalEntryId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "RetainedEarningsAccountId",
                table: "Businesses");
        }
    }
}
