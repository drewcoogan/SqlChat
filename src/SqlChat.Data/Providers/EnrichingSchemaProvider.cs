using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Providers;

public sealed class EnrichingSchemaProvider(ISchemaProvider inner, SchemaEnrichment enrichment) : ISchemaProvider
{
    private readonly ISchemaProvider _inner = inner.Validate();
    private readonly SchemaEnrichment _enrichment = enrichment.Validate();

    // Table and column names match case-insensitively, the same as the exclusion lists. A table key may be
    // schema-qualified ("sales.orders") to target one of several same-named tables.
    private readonly Dictionary<string, TableEnrichment> _tables = IgnoreCase(enrichment.Tables);
    private readonly HashSet<string> _excludedTables = new(enrichment.ExcludedTables, StringComparer.OrdinalIgnoreCase);

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken ct = default)
    {
        var catalog = await _inner.GetSchemaAsync(ct).ConfigureAwait(false);
        return Apply(catalog);
    }

    private SchemaCatalog Apply(SchemaCatalog catalog)
    {
        var tables = catalog.Tables
            .Where(t => !_excludedTables.Contains(t.Name) && !_excludedTables.Contains(QualifiedName(t)))
            .Select(ApplyTable)
            .ToList();

        var examples = _enrichment.Examples.Count > 0
            ? _enrichment.Examples
            : catalog.ExampleQueries;

        return catalog with { Tables = tables, ExampleQueries = examples };
    }

    private TableDescriptor ApplyTable(TableDescriptor table)
    {
        if (!_tables.TryGetValue(QualifiedName(table), out var tableEnrich)
            && !_tables.TryGetValue(table.Name, out tableEnrich))
            return table;

        var columnEnrichments = IgnoreCase(tableEnrich.Columns);

        var excludedCols = new HashSet<string>(tableEnrich.ExcludedColumns, StringComparer.OrdinalIgnoreCase);

        var columns = table.Columns
            .Where(c => !excludedCols.Contains(c.Name))
            .Select(c => ApplyColumn(c, columnEnrichments));

        return table with // returns a copy of this record, but with the following values updated, very cool
        {
            Description = tableEnrich.Description ?? table.Description,
            Columns = [.. columns]
        };
    }

    private static ColumnDescriptor ApplyColumn(ColumnDescriptor column, Dictionary<string, ColumnEnrichment> columnEnrichments)
    {
        if (!columnEnrichments.TryGetValue(column.Name, out var colEnrich))
            return column;

        var tags = colEnrich.SemanticTags is { Count: > 0 } // this is slick af
            ? [.. colEnrich.SemanticTags]
            : column.SemanticTags;

        return column with
        {
            Description = colEnrich.Description ?? column.Description,
            SemanticTags = tags
        };
    }

    private static string QualifiedName(TableDescriptor table) =>
        table.Schema is null ? table.Name : $"{table.Schema}.{table.Name}";

    // First entry wins if a source dictionary holds keys that differ only by case.
    private static Dictionary<string, T> IgnoreCase<T>(Dictionary<string, T> source)
    {
        Dictionary<string, T> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
            result.TryAdd(key, value);
        return result;
    }
}
