using System.Data;
using Microsoft.Data.SqlClient;

namespace SqlChat.Data.Providers;

/// <summary>
/// Introspects a SQL Server database schema via INFORMATION_SCHEMA.
/// </summary>
public sealed class SqlServerSchemaProvider : RelationalSchemaProvider
{
    public SqlServerSchemaProvider(string connectionString) : base(connectionString) { }

    protected override IDbConnection CreateConnection(string connectionString) =>
        new SqlConnection(connectionString);

    protected override string TablesQuery => """
        SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = 'BASE TABLE'
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    protected override string ColumnsQuery => """
        SELECT
            TABLE_SCHEMA   AS SchemaName,
            TABLE_NAME     AS TableName,
            COLUMN_NAME    AS ColumnName,
            DATA_TYPE      AS DataType,
            CASE IS_NULLABLE WHEN 'YES' THEN 1 ELSE 0 END AS IsNullable
        FROM INFORMATION_SCHEMA.COLUMNS
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;

    protected override string PrimaryKeysQuery => """
        SELECT
            ku.TABLE_SCHEMA AS SchemaName,
            ku.TABLE_NAME   AS TableName,
            ku.COLUMN_NAME  AS ColumnName
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
            ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
           AND tc.TABLE_SCHEMA    = ku.TABLE_SCHEMA
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        """;
}
