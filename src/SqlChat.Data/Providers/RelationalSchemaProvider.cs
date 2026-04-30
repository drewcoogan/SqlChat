using System.Data;
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
    private readonly string _connectionString = connectionString.Validate(nameof(connectionString), v => v.IsNotEmpty());

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection(_connectionString);
        connection.Open();

        var tableRows = await connection.QueryWithRetryAsync<TableRow>(TablesQuery, cancellationToken: cancellationToken).ConfigureAwait(false);
        var columnRows = await connection.QueryWithRetryAsync<ColumnRow>(ColumnsQuery, cancellationToken: cancellationToken).ConfigureAwait(false);
        var pkRows = await connection.QueryWithRetryAsync<PrimaryKeyRow>(PrimaryKeysQuery, cancellationToken: cancellationToken).ConfigureAwait(false);

        return BuildCatalog(tableRows, columnRows, pkRows);
    }

    protected abstract IDbConnection CreateConnection(string connectionString);
    protected abstract string TablesQuery { get; }
    protected abstract string ColumnsQuery { get; }
    protected abstract string PrimaryKeysQuery { get; }

    private static SchemaCatalog BuildCatalog(IEnumerable<TableRow> tableRows, IEnumerable<ColumnRow> columnRows, IEnumerable<PrimaryKeyRow> pkRows)
    {
        var pkSet = pkRows
            .ToHashSet(EqualityComparer<PrimaryKeyRow>.Create(
                (a, b) => a?.SchemaName == b?.SchemaName
                    && a?.TableName == b?.TableName
                    && a?.ColumnName == b?.ColumnName,
                r => HashCode.Combine(r?.SchemaName, r?.TableName, r?.ColumnName)));

        var columnsByTable = columnRows
            .GroupBy(c => (c.SchemaName, c.TableName))
            .ToDictionary(g => g.Key, g => g.ToList());

        var tables = new List<TableDescriptor>();
        foreach (var t in tableRows)
        {
            var key = (t.SchemaName, t.TableName);
            var cols = columnsByTable.TryGetValue(key, out var c) ? c : [];

            var columns = cols.Select(col =>
            {
                var isPk = pkRows.Any(pk =>
                    pk.SchemaName == col.SchemaName
                    && pk.TableName == col.TableName
                    && pk.ColumnName == col.ColumnName);
                return ColumnDescriptor.Create(col.ColumnName, col.DataType, isNullable: col.IsNullable, isPrimaryKey: isPk);
            });

            tables.Add(new TableDescriptor(t.TableName, t.SchemaName, [.. columns]));
        }

        return new SchemaCatalog(tables);
    }

    private sealed record TableRow(string SchemaName, string TableName);
    private sealed record ColumnRow(string SchemaName, string TableName, string ColumnName, string DataType, bool IsNullable);
    private sealed record PrimaryKeyRow(string SchemaName, string TableName, string ColumnName);
}
