namespace SqlChat.Domain.Builders;

using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

/// <summary>
/// Fluent builder for a <see cref="SchemaCatalog"/> without Entity Framework.
/// </summary>
public sealed class SchemaCatalogBuilder
{
    private readonly List<TableDescriptor> _tables = [];

    public SchemaCatalogBuilder AddTable(string name, Action<TableDescriptorBuilder> configure, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var table = new TableDescriptorBuilder(name, schema);
        configure.Validate()(table);
        _tables.Add(table.Build());
        return this;
    }

    public SchemaCatalog Build() => new(_tables);
}

public sealed class TableDescriptorBuilder(string name, string? schema)
{
    private readonly string _name = name;
    private readonly string? _schema = schema;
    private readonly List<ColumnDescriptor> _columns = [];

    public TableDescriptorBuilder AddColumn(string name, string storeType,
        string? description = null, bool nullable = true, bool primaryKey = false, ForeignKeyReference? foreignKey = null,
        params string[] semanticTags)
    {
        _columns.Add(ColumnDescriptor.Create(
            name.Validate(x => x.IsNotEmpty()),
            storeType.Validate(x => x.IsNotEmpty()),
            description,
            nullable,
            primaryKey,
            foreignKey,
            semanticTags));
        return this;
    }

    internal TableDescriptor Build() => new(_name, _schema, _columns);
}

