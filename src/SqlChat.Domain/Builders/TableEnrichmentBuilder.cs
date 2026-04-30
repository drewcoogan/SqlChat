using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Domain.Builders;

public sealed class TableEnrichmentBuilder
{
    private string? _description;
    private readonly List<string> _excludedColumns = [];
    private readonly Dictionary<string, ColumnEnrichmentBuilder> _columns = new(StringComparer.OrdinalIgnoreCase);

    public TableEnrichmentBuilder Description(string description)
    {
        _description = description.Validate();
        return this;
    }

    public TableEnrichmentBuilder ExcludeColumn(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _excludedColumns.Add(name);
        return this;
    }

    public TableEnrichmentBuilder Column(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        GetOrAddColumn(name).Description(description);
        return this;
    }

    public TableEnrichmentBuilder Column(string name, Action<ColumnEnrichmentBuilder> configure)
    {
        configure.Validate()(GetOrAddColumn(name));
        return this;
    }

    private ColumnEnrichmentBuilder GetOrAddColumn(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_columns.TryGetValue(name, out var builder))
            _columns[name] = builder = new ColumnEnrichmentBuilder();
        return builder;
    }

    internal TableEnrichment Build() => new()
    {
        Description = _description,
        ExcludedColumns = [.. _excludedColumns],
        Columns = _columns.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Build(),
            StringComparer.OrdinalIgnoreCase)
    };
}
