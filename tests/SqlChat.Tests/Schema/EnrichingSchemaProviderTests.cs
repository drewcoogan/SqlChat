using SqlChat.Data.Providers;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;

namespace SqlChat.Tests.Schema;

public sealed class EnrichingSchemaProviderTests
{
    private static SchemaCatalog Enrich(SchemaCatalog catalog, SchemaEnrichment enrichment)
    {
        var provider = new EnrichingSchemaProvider(new ManualSchemaProvider(catalog), enrichment);
        return provider.GetSchemaAsync().GetAwaiter().GetResult();
    }

    private static SchemaCatalog BaseSchema() => new SchemaCatalogBuilder()
        .AddTable("customers", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("email", "varchar")
            .AddColumn("internal_id", "varchar"))
        .AddTable("orders", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("status", "text"))
        .Build();

    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSchemaAsync_NoEnrichment_PassesThroughUnchanged()
    {
        SchemaCatalog original = BaseSchema();
        var provider = new EnrichingSchemaProvider(
            new ManualSchemaProvider(original), new SchemaEnrichment());

        SchemaCatalog result = await provider.GetSchemaAsync();

        Assert.Equal(original.Tables.Count, result.Tables.Count);
        Assert.Equal(
            original.Tables.SelectMany(t => t.Columns).Count(),
            result.Tables.SelectMany(t => t.Columns).Count());
    }

    [Fact]
    public void GetSchemaAsync_ExcludeTable_RemovesTable()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .ExcludeTable("orders")
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        Assert.DoesNotContain(result.Tables, t => t.Name == "orders");
        Assert.Contains(result.Tables, t => t.Name == "customers");
    }

    [Fact]
    public void GetSchemaAsync_TableDescription_OverridesDescription()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("customers", t => t.Description("All registered customers."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        TableDescriptor customers = result.Tables.Single(t => t.Name == "customers");
        Assert.Equal("All registered customers.", customers.Description);
    }

    [Fact]
    public void GetSchemaAsync_ColumnDescription_OverridesDescription()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("orders", t => t
                .Column("status", "One of: pending, shipped, delivered, cancelled."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        ColumnDescriptor status = result.Tables
            .Single(t => t.Name == "orders").Columns
            .Single(c => c.Name == "status");
        Assert.Equal("One of: pending, shipped, delivered, cancelled.", status.Description);
    }

    [Fact]
    public void GetSchemaAsync_ExcludeColumn_RemovesColumn()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("customers", t => t.ExcludeColumn("internal_id"))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        TableDescriptor customers = result.Tables.Single(t => t.Name == "customers");
        Assert.DoesNotContain(customers.Columns, c => c.Name == "internal_id");
        Assert.Contains(customers.Columns, c => c.Name == "email");
    }

    [Fact]
    public void GetSchemaAsync_SemanticTags_AddedToColumn()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("orders", t => t
                .Column("status", c => c
                    .Description("Order status.")
                    .Tag("filterable")
                    .Tag("indexed")))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        ColumnDescriptor status = result.Tables
            .Single(t => t.Name == "orders").Columns
            .Single(c => c.Name == "status");
        Assert.Contains("filterable", status.SemanticTags);
        Assert.Contains("indexed", status.SemanticTags);
    }

    [Fact]
    public void GetSchemaAsync_TableNotInEnrichment_PassesThroughUnchanged()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("orders", t => t.Description("Purchase orders."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        // customers table had no enrichment — description should stay null
        TableDescriptor customers = result.Tables.Single(t => t.Name == "customers");
        Assert.Null(customers.Description);
        Assert.Equal(3, customers.Columns.Count);
    }

    [Fact]
    public void GetSchemaAsync_CaseInsensitiveTableMatch_EnrichmentApplies()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("CUSTOMERS", t => t.Description("Case-insensitive match."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        TableDescriptor customers = result.Tables.Single(t => t.Name == "customers");
        Assert.Equal("Case-insensitive match.", customers.Description);
    }

    [Fact]
    public void GetSchemaAsync_CaseInsensitiveColumnMatch_EnrichmentApplies()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("customers", t => t
                .Column("EMAIL", "Primary contact address."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        ColumnDescriptor email = result.Tables
            .Single(t => t.Name == "customers").Columns
            .Single(c => c.Name == "email");
        Assert.Equal("Primary contact address.", email.Description);
    }

    [Fact]
    public void GetSchemaAsync_Examples_CopiedToCatalog()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .AddExample("Show all customers", "SELECT * FROM customers")
            .AddExample("Show delivered orders", "SELECT * FROM orders WHERE status = 'delivered'")
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        Assert.NotNull(result.ExampleQueries);
        Assert.Equal(2, result.ExampleQueries!.Count);
        Assert.Equal("Show all customers", result.ExampleQueries[0].Description);
        Assert.Equal("SELECT * FROM customers", result.ExampleQueries[0].Sql);
    }

    [Fact]
    public void GetSchemaAsync_NoExamples_ExampleQueriesIsNullOrEmpty()
    {
        SchemaEnrichment enrichment = new SchemaEnrichmentBuilder()
            .Table("customers", t => t.Description("All customers."))
            .Build();

        SchemaCatalog result = Enrich(BaseSchema(), enrichment);

        Assert.True(result.ExampleQueries is null || result.ExampleQueries.Count == 0);
    }
}
