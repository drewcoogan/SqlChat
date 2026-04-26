using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Providers;

/// <summary>
/// Builds a <see cref="SchemaCatalog"/> from an EF Core <see cref="IModel"/> (for example <c>dbContext.Model</c>).
/// </summary>
public sealed class EfCoreSchemaProvider : ISchemaProvider
{
    private readonly SchemaCatalog _catalog;

    public EfCoreSchemaProvider(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _catalog = BuildCatalog(model);
    }

    public Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog);

    private static SchemaCatalog BuildCatalog(IModel model)
    {
        var tableGroups = model.GetEntityTypes()
            .Where(static e => !e.IsOwned())
            .Select(e => (Entity: e, Table: e.GetTableName(), Schema: e.GetSchema()))
            .Where(x => x.Table is not null)
            .GroupBy(x => (x.Schema, Table: x.Table!));

        var tables = new List<TableDescriptor>();

        foreach (var group in tableGroups)
        {
            var columns = new Dictionary<string, ColumnDescriptor>(StringComparer.OrdinalIgnoreCase);

            foreach (var (Entity, Table, Schema) in group)
            {
                foreach (var property in Entity.GetProperties())
                {
                    var columnName = property.GetColumnName();
                    
                    if (columnName is null || columns.ContainsKey(columnName))
                        continue;

                    columns[columnName] = MapProperty(property);
                }
            }

            var (schema, tableName) = group.Key;
            var tableDescription = TryGetComment(group.First().Entity);
            tables.Add(new TableDescriptor(tableName, schema, [.. columns.Values], tableDescription));
        }

        return new SchemaCatalog(tables);
    }

    private static ColumnDescriptor MapProperty(IProperty property)
    {
        var name = property.GetColumnName() ?? property.Name;
        var storeType = TryGetRelationalStoreType(property)
            ?? TryGetStoreTypeFromTypeMapping(property.GetTypeMapping())
            ?? property.ClrType.Name;
        var isNullable = property.IsNullable;
        var isPk = property.IsPrimaryKey();

        ForeignKeyReference? fkRef = null;
        var fk = property.GetContainingForeignKeys().FirstOrDefault();
        if (fk is not null && fk.PrincipalKey.Properties.Count == 1)
        {
            var principal = fk.PrincipalEntityType;
            var principalTable = principal.GetTableName();
            var principalColumn = fk.PrincipalKey.Properties[0].GetColumnName();

            if (principalTable is not null && principalColumn is not null)
            {
                fkRef = new ForeignKeyReference(principalTable, principal.GetSchema(), principalColumn);
            }
        }

        return ColumnDescriptor.Create(name, storeType, description: TryGetComment(property), isNullable, isPk, fkRef, semanticTags: null);
    }

    private static string? TryGetStoreTypeFromTypeMapping(CoreTypeMapping typeMapping) =>
        typeMapping is RelationalTypeMapping relational ? relational.StoreType : null;

    private static string? TryGetRelationalStoreType(IProperty property)
    {
        try { return property.GetColumnType(); }
        catch (InvalidCastException) { return null; }
    }

    private static string? TryGetComment(IReadOnlyEntityType entity)
    {
        try { return entity.GetComment(); }
        catch (InvalidOperationException) { return null; }
    }

    private static string? TryGetComment(IReadOnlyProperty property)
    {
        try { return property.GetComment(); }
        catch (InvalidOperationException) { return null; }
    }
}

