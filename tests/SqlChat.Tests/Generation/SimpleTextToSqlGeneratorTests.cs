using SqlChat.Client;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Generation;

public sealed class SimpleTextToSqlGeneratorTests
{
    [Fact]
    public async Task GenerateSqlAsync_selects_from_inferred_table()
    {
        var catalog = new SchemaCatalogBuilder()
            .AddTable("Users", t => t
                .AddColumn("Id", "int", nullable: false, primaryKey: true)
                .AddColumn("Email", "nvarchar(256)", nullable: false))
            .Build();

        var client = new SqlChatBuilder().UseSchema(catalog).Build();

        var sql = await client.GenerateSqlAsync("show me all users");
        Assert.Equal("SELECT * FROM Users", sql);
        Assert.True(client.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public async Task GenerateSqlAsync_supports_simple_where_equals_numeric()
    {
        var catalog = new SchemaCatalogBuilder()
            .AddTable("Users", t => t
                .AddColumn("Id", "int", nullable: false, primaryKey: true)
                .AddColumn("Email", "nvarchar(256)", nullable: false))
            .Build();

        var client = new SqlChatBuilder().UseSchema(catalog).Build();

        var sql = await client.GenerateSqlAsync("users where id = 5");
        Assert.Equal("SELECT * FROM Users WHERE Id = 5", sql);
        Assert.True(client.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public async Task GenerateSqlAsync_supports_simple_where_equals_string_and_escapes_quotes()
    {
        var catalog = new SchemaCatalogBuilder()
            .AddTable("Users", t => t
                .AddColumn("Id", "int", nullable: false, primaryKey: true)
                .AddColumn("Email", "nvarchar(256)", nullable: false)
                .AddColumn("Name", "nvarchar(200)", nullable: false))
            .Build();

        var client = new SqlChatBuilder().UseSchema(catalog).Build();

        var sql = await client.GenerateSqlAsync("users where name is O'Hara");
        Assert.Equal("SELECT * FROM Users WHERE Name = 'O''Hara'", sql);
        Assert.True(client.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public async Task GenerateSqlAsync_supports_count()
    {
        var catalog = new SchemaCatalogBuilder()
            .AddTable("Orders", t => t.AddColumn("Id", "int", nullable: false, primaryKey: true))
            .Build();

        var client = new SqlChatBuilder().UseSchema(catalog).Build();

        var sql = await client.GenerateSqlAsync("count orders");
        Assert.Equal("SELECT COUNT(*) FROM Orders", sql);
    }
}

