using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Providers;

public sealed class EnrichingSchemaProvider : ISchemaProvider
{
    private readonly ISchemaProvider _inner;
    private readonly SchemaEnrichment _enrichment;

    public EnrichingSchemaProvider(ISchemaProvider inner, SchemaEnrichment enrichment)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _enrichment = enrichment ?? throw new ArgumentNullException(nameof(enrichment));
    }

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _inner.GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        return Apply(catalog);
    }

    private SchemaCatalog Apply(SchemaCatalog catalog)
    {
        var excluded = new HashSet<string>(_enrichment.ExcludedTables, StringComparer.OrdinalIgnoreCase);

        var tables = catalog.Tables
            .Where(t => !excluded.Contains(t.Name))
            .Select(ApplyTable)
            .ToList();

        var examples = _enrichment.Examples.Count > 0
            ? (IReadOnlyList<ExampleQuery>)_enrichment.Examples
            : catalog.ExampleQueries;

        return catalog with { Tables = tables, ExampleQueries = examples };
    }

    private TableDescriptor ApplyTable(TableDescriptor table)
    {
        if (!_enrichment.Tables.TryGetValue(table.Name, out var tableEnrich))
            return table;

        var excludedCols = new HashSet<string>(tableEnrich.ExcludedColumns, StringComparer.OrdinalIgnoreCase);

        var columns = table.Columns
            .Where(c => !excludedCols.Contains(c.Name))
            .Select(c => ApplyColumn(c, tableEnrich))
            .ToList();

        return table with
        {
            Description = tableEnrich.Description ?? table.Description,
            Columns = columns
        };
    }

    private static ColumnDescriptor ApplyColumn(ColumnDescriptor column, TableEnrichment tableEnrich)
    {
        if (!tableEnrich.Columns.TryGetValue(column.Name, out var colEnrich))
            return column;

        var tags = colEnrich.SemanticTags is { Count: > 0 }
            ? [.. colEnrich.SemanticTags]
            : column.SemanticTags;

        return column with
        {
            Description = colEnrich.Description ?? column.Description,
            SemanticTags = tags
        };
    }
}
