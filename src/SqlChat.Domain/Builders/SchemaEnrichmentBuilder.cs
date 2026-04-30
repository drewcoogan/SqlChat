using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Domain.Builders;

public sealed class SchemaEnrichmentBuilder
{
    private readonly List<string> _excludedTables = [];
    private readonly Dictionary<string, TableEnrichmentBuilder> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExampleQuery> _examples = [];

    public SchemaEnrichmentBuilder ExcludeTable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _excludedTables.Add(name);
        return this;
    }

    public SchemaEnrichmentBuilder Table(string name, Action<TableEnrichmentBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_tables.TryGetValue(name, out var builder))
            _tables[name] = builder = new TableEnrichmentBuilder();
        configure.Validate()(builder);
        return this;
    }

    public SchemaEnrichmentBuilder AddExample(string description, string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        _examples.Add(new ExampleQuery(description, sql));
        return this;
    }

    public SchemaEnrichment Build() => new()
    {
        ExcludedTables = [.. _excludedTables],
        Tables = _tables.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Build(),
            StringComparer.OrdinalIgnoreCase),
        Examples = [.. _examples]
    };
}
