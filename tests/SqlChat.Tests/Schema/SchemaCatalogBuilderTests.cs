using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Schema;

public sealed class SchemaCatalogBuilderTests
{
    [Fact]
    public void Build_creates_table_and_columns_with_metadata()
    {
        var catalog = new SchemaCatalogBuilder()
            .AddTable("Users", t => t
                .AddColumn("Id", "int", description: "Primary key", nullable: false, primaryKey: true)
                .AddColumn("Email", "nvarchar(256)", description: "Email address", nullable: false, primaryKey: false, foreignKey: null, "pii", "email"))
            .Build();

        Assert.Single(catalog.Tables);

        var table = catalog.Tables[0];
        Assert.Equal("Users", table.Name);
        Assert.Null(table.Schema);

        Assert.Equal(2, table.Columns.Count);

        var id = Assert.Single(table.Columns, c => c.Name == "Id");
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);

        var email = Assert.Single(table.Columns, c => c.Name == "Email");
        Assert.False(email.IsPrimaryKey);
        Assert.False(email.IsNullable);
        Assert.Contains("pii", email.SemanticTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("email", email.SemanticTags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ColumnDescriptor_Create_deduplicates_semantic_tags_case_insensitively()
    {
        var c = ColumnDescriptor.Create(
            "Email",
            "nvarchar(256)",
            semanticTags: ["PII", "pii", "Email", "email"]);

        Assert.Equal(2, c.SemanticTags.Count);
        Assert.Contains("pii", c.SemanticTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("email", c.SemanticTags, StringComparer.OrdinalIgnoreCase);
    }
}

