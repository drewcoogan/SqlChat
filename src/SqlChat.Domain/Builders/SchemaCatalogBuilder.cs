namespace SqlChat.Domain.Builders;

using SqlChat.Domain.Models;

/// <summary>
/// Fluent builder for a <see cref="SchemaCatalog"/> without Entity Framework.
/// </summary>
public sealed class SchemaCatalogBuilder
{
    private readonly List<TableDescriptor> _tables = new();

    public SchemaCatalogBuilder AddTable(string name, Action<TableDescriptorBuilder> configure, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var table = new TableDescriptorBuilder(name, schema);
        configure(table);
        _tables.Add(table.Build());
        return this;
    }

    public SchemaCatalog Build() => new(_tables);
}

public sealed class TableDescriptorBuilder
{
    private readonly string _name;
    private readonly string? _schema;
    private readonly List<ColumnDescriptor> _columns = new();

    internal TableDescriptorBuilder(string name, string? schema)
    {
        _name = name;
        _schema = schema;
    }

    public TableDescriptorBuilder AddColumn(
        string name,
        string storeType,
        string? description = null,
        bool nullable = true,
        bool primaryKey = false,
        ForeignKeyReference? foreignKey = null,
        params string[] semanticTags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

        _columns.Add(ColumnDescriptor.Create(
            name,
            storeType,
            description,
            nullable,
            primaryKey,
            foreignKey,
            semanticTags));
        return this;
    }

    internal TableDescriptor Build() => new(_name, _schema, _columns);
}

