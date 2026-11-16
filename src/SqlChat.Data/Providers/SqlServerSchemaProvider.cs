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

    // SQL Server has no POSITION_IN_UNIQUE_CONSTRAINT; FK and referenced columns pair up by ORDINAL_POSITION.
    protected override string? ForeignKeysQuery => """
        SELECT
            rc.CONSTRAINT_SCHEMA AS ConstraintSchema,
            rc.CONSTRAINT_NAME   AS ConstraintName,
            fk.TABLE_SCHEMA      AS SchemaName,
            fk.TABLE_NAME        AS TableName,
            fk.COLUMN_NAME       AS ColumnName,
            pk.TABLE_SCHEMA      AS PrincipalSchema,
            pk.TABLE_NAME        AS PrincipalTable,
            pk.COLUMN_NAME       AS PrincipalColumn
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk
            ON fk.CONSTRAINT_SCHEMA = rc.CONSTRAINT_SCHEMA
           AND fk.CONSTRAINT_NAME   = rc.CONSTRAINT_NAME
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE pk
            ON pk.CONSTRAINT_SCHEMA = rc.UNIQUE_CONSTRAINT_SCHEMA
           AND pk.CONSTRAINT_NAME   = rc.UNIQUE_CONSTRAINT_NAME
           AND pk.ORDINAL_POSITION  = fk.ORDINAL_POSITION
        WHERE fk.TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
        """;
}
