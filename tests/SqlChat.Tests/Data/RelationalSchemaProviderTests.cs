using SqlChat.Data.Providers;
using SqlChat.Domain.Models;
using static SqlChat.Data.Providers.RelationalSchemaProvider;

namespace SqlChat.Tests.Data;

public sealed class RelationalSchemaProviderTests
{
    private static readonly TableRow[] Tables = [new("public", "orders"), new("public", "customers")];

    private static readonly ColumnRow[] Columns =
    [
        new("public", "orders", "id", "int4", false),
        new("public", "orders", "customer_id", "int4", false),
        new("public", "orders", "region", "text", true),
        new("public", "orders", "code", "text", true),
        new("public", "customers", "id", "int4", false),
    ];

    private static readonly PrimaryKeyRow[] PrimaryKeys = [new("public", "orders", "id"), new("public", "customers", "id")];

    private static ColumnDescriptor Column(SchemaCatalog catalog, string table, string column) =>
        catalog.Tables.Single(t => t.Name == table).Columns.Single(c => c.Name == column);

    [Fact]
    public void BuildCatalog_SingleColumnForeignKey_MapsForeignKeyReference()
    {
        ForeignKeyRow[] foreignKeys =
            [new("public", "orders_customer_fk", "public", "orders", "customer_id", "public", "customers", "id")];

        SchemaCatalog catalog = BuildCatalog(Tables, Columns, PrimaryKeys, foreignKeys);

        Assert.Equal(
            new ForeignKeyReference("customers", "public", "id"),
            Column(catalog, "orders", "customer_id").ForeignKey);
        Assert.Null(Column(catalog, "orders", "id").ForeignKey);
    }

    [Fact]
    public void BuildCatalog_CompositeForeignKey_IsSkipped()
    {
        ForeignKeyRow[] foreignKeys =
        [
            new("public", "orders_region_fk", "public", "orders", "region", "public", "regions", "region"),
            new("public", "orders_region_fk", "public", "orders", "code", "public", "regions", "code"),
        ];

        SchemaCatalog catalog = BuildCatalog(Tables, Columns, PrimaryKeys, foreignKeys);

        Assert.Null(Column(catalog, "orders", "region").ForeignKey);
        Assert.Null(Column(catalog, "orders", "code").ForeignKey);
    }

    [Fact]
    public void BuildCatalog_PrimaryKeyRows_FlagOnlyMatchingColumns()
    {
        SchemaCatalog catalog = BuildCatalog(Tables, Columns, PrimaryKeys, []);

        Assert.True(Column(catalog, "orders", "id").IsPrimaryKey);
        Assert.False(Column(catalog, "orders", "customer_id").IsPrimaryKey);
    }
}
