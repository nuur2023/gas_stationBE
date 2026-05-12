using Microsoft.EntityFrameworkCore.Migrations;

namespace gas_station.Migrations;

/// <summary>
/// INFORMATION_SCHEMA + prepared statements for DDL on MySQL builds that do not support
/// <c>ADD COLUMN IF NOT EXISTS</c>, <c>DROP COLUMN IF EXISTS</c>, <c>CREATE INDEX IF NOT EXISTS</c>, etc.
/// (e.g. managed MySQL on DigitalOcean App Platform).
/// </summary>
internal static class MigrationMySqlCompat
{
    internal static void RunIfColumnExists(MigrationBuilder mb, string table, string column, string alterSql)
    {
        mb.Sql(
            "SET @__gs_exec := (SELECT IF("
            + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
            + table + "' AND COLUMN_NAME = '" + column + "') > 0, "
            + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
            + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
    }

    internal static void AddColumnIfNotExists(MigrationBuilder mb, string table, string column, string alterSql)
    {
        mb.Sql(
            "SET @__gs_exec := (SELECT IF("
            + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
            + table + "' AND COLUMN_NAME = '" + column + "') = 0, "
            + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
            + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
    }

    internal static void CreateIndexIfNotExists(MigrationBuilder mb, string table, string indexName, string createSql)
    {
        mb.Sql(
            "SET @__gs_exec := (SELECT IF("
            + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS "
            + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
            + table + "' AND INDEX_NAME = '" + indexName + "') = 0, "
            + "'" + EscapeSqlLiteral(createSql) + "', 'SELECT 1'));"
            + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
    }

    internal static void DropIndexIfExists(MigrationBuilder mb, string table, string indexName)
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

    internal static void DropForeignKeyIfExists(MigrationBuilder mb, string table, string constraintName)
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

    internal static void AddForeignKeyIfNotExists(MigrationBuilder mb, string table, string constraintName, string alterSql)
    {
        mb.Sql(
            "SET @__gs_exec := (SELECT IF("
            + "(SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS "
            + "WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = '"
            + table + "' AND CONSTRAINT_NAME = '" + constraintName + "' AND CONSTRAINT_TYPE = 'FOREIGN KEY') = 0, "
            + "'" + EscapeSqlLiteral(alterSql) + "', 'SELECT 1'));"
            + "PREPARE __gs_stmt FROM @__gs_exec; EXECUTE __gs_stmt; DEALLOCATE PREPARE __gs_stmt;");
    }

    internal static string EscapeSqlLiteral(string sql) => sql.Replace("'", "''", StringComparison.Ordinal);
}
