using SqlChat.Client;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;
using SqlChat.Domain.Results;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Engine;

public sealed class PreviewSqlAsyncTests
{
    private static readonly SchemaCatalog TestSchema = new SchemaCatalogBuilder()
        .AddTable("users", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("email", "varchar"))
        .Build();

    private static ISqlChatClient BuildClient(ISqlGenerator generator) =>
        new SqlChatBuilder()
            .UseSchema(TestSchema)
            .UseGenerator(generator)
            .Build();

    [Fact]
    public async Task PreviewSqlAsync_ValidQuestion_ReturnsSuccessWithSql()
    {
        ISqlChatClient client = BuildClient(new FakeSqlGenerator("SELECT id, email FROM users"));

        SqlChatResult result = await client.PreviewSqlAsync("find all users");

        Assert.True(result.IsSuccess);
        Assert.Equal("SELECT id, email FROM users", result.Sql);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PreviewSqlAsync_GeneratorThrows_ReturnsFailedResultWithoutThrowing()
    {
        ISqlChatClient client = BuildClient(new ThrowingSqlGenerator("LLM unavailable"));

        SqlChatResult result = await client.PreviewSqlAsync("find all users");

        Assert.False(result.IsSuccess);
        Assert.Contains("LLM unavailable", result.Error);
    }

    [Fact]
    public async Task PreviewSqlAsync_SecurityViolation_ReturnsFailedResult()
    {
        ISqlChatClient client = BuildClient(new FakeSqlGenerator("DROP TABLE users"));

        SqlChatResult result = await client.PreviewSqlAsync("delete everything");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    private sealed class ThrowingSqlGenerator(string message) : ISqlGenerator
    {
        public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }
}
