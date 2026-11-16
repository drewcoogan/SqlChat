using System.Data;
using Npgsql;

namespace SqlChat.Data.Providers;

/// <summary>
/// Introspects a PostgreSQL database schema via information_schema.
/// </summary>
public sealed class PostgresSchemaProvider(string connectionString) : RelationalSchemaProvider(connectionString)
{
    protected override IDbConnection CreateConnection(string connectionString) =>
        new NpgsqlConnection(connectionString);

    protected override string TablesQuery => """
        SELECT table_schema AS SchemaName, table_name AS TableName
        FROM information_schema.tables
        WHERE table_type = 'BASE TABLE'
          AND table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name
        """;

    protected override string ColumnsQuery => """
        SELECT
            table_schema AS SchemaName,
            table_name   AS TableName,
            column_name  AS ColumnName,
            udt_name     AS DataType,
            CASE is_nullable WHEN 'YES' THEN true ELSE false END AS IsNullable
        FROM information_schema.columns
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, ordinal_position
        """;

    protected override string PrimaryKeysQuery => """
        SELECT
            kcu.table_schema AS SchemaName,
            kcu.table_name   AS TableName,
            kcu.column_name  AS ColumnName
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
            ON tc.constraint_name = kcu.constraint_name
           AND tc.table_schema    = kcu.table_schema
        WHERE tc.constraint_type = 'PRIMARY KEY'
          AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
        """;

    // position_in_unique_constraint pairs each FK column with its referenced column.
    protected override string? ForeignKeysQuery => """
        SELECT
            rc.constraint_schema AS ConstraintSchema,
            rc.constraint_name   AS ConstraintName,
            fk.table_schema      AS SchemaName,
            fk.table_name        AS TableName,
            fk.column_name       AS ColumnName,
            pk.table_schema      AS PrincipalSchema,
            pk.table_name        AS PrincipalTable,
            pk.column_name       AS PrincipalColumn
        FROM information_schema.referential_constraints rc
        JOIN information_schema.key_column_usage fk
            ON fk.constraint_schema = rc.constraint_schema
           AND fk.constraint_name   = rc.constraint_name
        JOIN information_schema.key_column_usage pk
            ON pk.constraint_schema = rc.unique_constraint_schema
           AND pk.constraint_name   = rc.unique_constraint_name
           AND pk.ordinal_position  = fk.position_in_unique_constraint
        WHERE fk.table_schema NOT IN ('pg_catalog', 'information_schema')
        """;
}
