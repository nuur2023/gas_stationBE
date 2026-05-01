using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gas_station.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Baseline migration: intentionally empty.
            // Existing schemas are already provisioned; this migration only anchors ModelSnapshot/history.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Baseline migration: intentionally empty.
        }
    }
}
