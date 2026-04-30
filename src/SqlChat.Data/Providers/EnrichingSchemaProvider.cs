using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Providers;

public sealed class EnrichingSchemaProvider(ISchemaProvider inner, SchemaEnrichment enrichment) : ISchemaProvider
{
    private readonly ISchemaProvider _inner = inner.Validate(nameof(inner));
    private readonly SchemaEnrichment _enrichment = enrichment.Validate(nameof(enrichment));

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
            ? _enrichment.Examples
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
            .Select(c => ApplyColumn(c, tableEnrich));

        return table with // returns a copy of this record, but with the following values updated, very cool
        {
            Description = tableEnrich.Description ?? table.Description,
            Columns = [.. columns]
        };
    }

    private static ColumnDescriptor ApplyColumn(ColumnDescriptor column, TableEnrichment tableEnrich)
    {
        if (!tableEnrich.Columns.TryGetValue(column.Name, out var colEnrich))
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
}
