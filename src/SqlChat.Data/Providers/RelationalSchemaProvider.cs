using System.Data;
using System.Data.Common;
using Dapper;
using Polly;
using SqlChat.Data.Internal;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Providers;

/// <summary>
/// Base schema provider for relational databases via INFORMATION_SCHEMA.
/// </summary>
public abstract class RelationalSchemaProvider(string connectionString) : ISchemaProvider
{
    private const int SchemaRetryMax = 3;

    private static readonly ResiliencePipeline Pipeline =
        QueryRetryPipeline.Build<DbException>(SchemaRetryMax, ex => ex.IsTransient);

    private readonly string _connectionString = connectionString.Validate(v => v.IsNotEmpty());

    // The retry wraps connect → queries: a transient failure usually breaks the connection.
    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken ct = default) =>
        await Pipeline.ExecuteAsync(async token =>
        {
            using var connection = CreateConnection(_connectionString);
            if (connection is DbConnection dbConnection)
                await dbConnection.OpenAsync(token).ConfigureAwait(false);
            else
                connection.Open();

            var tableRows = await QueryAsync<TableRow>(connection, TablesQuery, token).ConfigureAwait(false);
            var columnRows = await QueryAsync<ColumnRow>(connection, ColumnsQuery, token).ConfigureAwait(false);
            var pkRows = await QueryAsync<PrimaryKeyRow>(connection, PrimaryKeysQuery, token).ConfigureAwait(false);
            var fkRows = ForeignKeysQuery is { } foreignKeysQuery
                ? await QueryAsync<ForeignKeyRow>(connection, foreignKeysQuery, token).ConfigureAwait(false)
                : [];

            return BuildCatalog(tableRows, columnRows, pkRows, fkRows);
        }, ct).ConfigureAwait(false);

    protected abstract IDbConnection CreateConnection(string connectionString);
    protected abstract string TablesQuery { get; }
    protected abstract string ColumnsQuery { get; }
    protected abstract string PrimaryKeysQuery { get; }

    /// <summary>
    /// Query returning one row per foreign-key column: ConstraintSchema, ConstraintName, SchemaName, TableName,
    /// ColumnName, PrincipalSchema, PrincipalTable, PrincipalColumn. Return <c>null</c> (the default) to skip
    /// foreign keys.
    /// </summary>
    protected virtual string? ForeignKeysQuery => null;

    private static Task<IEnumerable<T>> QueryAsync<T>(IDbConnection connection, string sql, CancellationToken ct) =>
        connection.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: ct));

    internal static SchemaCatalog BuildCatalog(
        IEnumerable<TableRow> tableRows,
        IEnumerable<ColumnRow> columnRows,
        IEnumerable<PrimaryKeyRow> pkRows,
        IEnumerable<ForeignKeyRow> fkRows)
    {
        HashSet<PrimaryKeyRow> pkSet = [.. pkRows];

        // Only single-column foreign keys map to ForeignKeyReference (same rule as EfCoreSchemaProvider).
        // Grouping by table as well as constraint keeps two same-named Postgres constraints apart.
        var fkByColumn = fkRows
            .GroupBy(fk => (fk.ConstraintSchema, fk.ConstraintName, fk.SchemaName, fk.TableName))
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .GroupBy(fk => (fk.SchemaName, fk.TableName, fk.ColumnName))
            .ToDictionary(
                g => g.Key,
                g => new ForeignKeyReference(g.First().PrincipalTable, g.First().PrincipalSchema, g.First().PrincipalColumn));

        var columnsByTable = columnRows
            .GroupBy(c => (c.SchemaName, c.TableName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var tables = new List<TableDescriptor>();
        foreach (var t in tableRows)
        {
            var cols = columnsByTable.TryGetValue((t.SchemaName, t.TableName), out var c) ? c : [];

            var columns = cols.Select(col => ColumnDescriptor.Create(
                col.ColumnName,
                col.DataType,
                isNullable: col.IsNullable,
                isPrimaryKey: pkSet.Contains(new PrimaryKeyRow(col.SchemaName, col.TableName, col.ColumnName)),
                foreignKey: fkByColumn.GetValueOrDefault((col.SchemaName, col.TableName, col.ColumnName))));

            tables.Add(new TableDescriptor(t.TableName, t.SchemaName, [.. columns]));
        }

        return new SchemaCatalog(tables);
    }

    internal sealed record TableRow(string SchemaName, string TableName);
    internal sealed record ColumnRow(string SchemaName, string TableName, string ColumnName, string DataType, bool IsNullable);
    internal sealed record PrimaryKeyRow(string SchemaName, string TableName, string ColumnName);
    internal sealed record ForeignKeyRow(
        string ConstraintSchema,
        string ConstraintName,
        string SchemaName,
        string TableName,
        string ColumnName,
        string PrincipalSchema,
        string PrincipalTable,
        string PrincipalColumn);
}
