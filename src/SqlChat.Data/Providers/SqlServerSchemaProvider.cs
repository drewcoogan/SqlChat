using System.Data;
using Microsoft.Data.SqlClient;

namespace SqlChat.Data.Providers;

public sealed class SqlServerSchemaProvider(string connectionString) : RelationalSchemaProvider(connectionString)
{
    protected override IDbConnection CreateConnection(string connectionString) =>
        new SqlConnection(connectionString);

    protected override string TablesQuery => """
        SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE'
          AND TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    protected override string ColumnsQuery => """
        SELECT
            TABLE_SCHEMA AS SchemaName,
            TABLE_NAME   AS TableName,
            COLUMN_NAME  AS ColumnName,
            DATA_TYPE    AS DataType,
            CAST(CASE IS_NULLABLE WHEN 'YES' THEN 1 ELSE 0 END AS BIT) AS IsNullable
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;

    protected override string PrimaryKeysQuery => """
        SELECT
            kcu.TABLE_SCHEMA AS SchemaName,
            kcu.TABLE_NAME   AS TableName,
            kcu.COLUMN_NAME  AS ColumnName
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
            ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
           AND tc.TABLE_SCHEMA    = kcu.TABLE_SCHEMA
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
          AND tc.TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        """;
}
