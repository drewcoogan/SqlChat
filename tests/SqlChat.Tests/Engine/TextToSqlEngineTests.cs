using SqlChat.Domain.Builders;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;
using SqlChat.Service.Engine;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Engine;

public sealed class TextToSqlEngineTests
{
    private static readonly SchemaCatalog TestSchema = new SchemaCatalogBuilder()
        .AddTable("users", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("email", "varchar"))
        .Build();

    private static TextToSqlEngine BuildEngine(string sql) =>
        new(new ManualSchemaProvider(TestSchema), new FakeSqlGenerator(sql));

    private static TextToSqlEngine BuildEngine(SequencedFakeSqlGenerator generator) =>
        new(new ManualSchemaProvider(TestSchema), generator);

    // -------------------------------------------------------------------------
    // GenerateSqlAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateSqlAsync_ValidQuery_ReturnsSql()
    {
        TextToSqlEngine engine = BuildEngine("SELECT id, email FROM users");

        string sql = await engine.GenerateSqlAsync("find all users");

        Assert.Equal("SELECT id, email FROM users", sql);
    }

    [Fact]
    public async Task GenerateSqlAsync_GeneratorReturnsDml_ThrowsSqlSecurityException()
    {
        TextToSqlEngine engine = BuildEngine("DROP TABLE users");

        await Assert.ThrowsAsync<SqlSecurityException>(
            () => engine.GenerateSqlAsync("delete everything"));
    }

    // -------------------------------------------------------------------------
    // GenerateAndExecuteManyAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAndExecuteManyAsync_Success_ReturnsDataAndSql()
    {
        TextToSqlEngine engine = BuildEngine("SELECT id, email FROM users");
        var executor = new FakeQueryExecutor();
        executor.Returns(new[] { new { id = 1, email = "a@example.com" } });

        var (data, sql) = await engine.GenerateAndExecuteManyAsync<object>(
            "find all users", executor, llmFixRetryMax: 0);

        Assert.Equal("SELECT id, email FROM users", sql);
        Assert.Single(data);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_ExecutionFailsThenSucceeds_RetriesAndReturnsData()
    {
        // First SQL fails at execution; second SQL (after retry prompt) succeeds.
        var generator = new SequencedFakeSqlGenerator(
            "SELECT bad syntax FROM users",
            "SELECT id, email FROM users");
        TextToSqlEngine engine = BuildEngine(generator);

        var executor = new FakeQueryExecutor();
        executor.Throws(new InvalidOperationException("syntax error at position 7"));
        executor.Returns(new[] { new { id = 1, email = "a@example.com" } });

        var (data, sql) = await engine.GenerateAndExecuteManyAsync<object>(
            "find all users", executor, llmFixRetryMax: 1);

        Assert.Equal(2, generator.CallCount);
        Assert.Single(data);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_AllRetriesExhausted_Throws()
    {
        TextToSqlEngine engine = BuildEngine("SELECT id FROM users");
        var executor = new FakeQueryExecutor();
        executor.Throws(new InvalidOperationException("db error"));
        executor.Throws(new InvalidOperationException("db error"));
        executor.Throws(new InvalidOperationException("db error"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.GenerateAndExecuteManyAsync<object>(
                "find all users", executor, llmFixRetryMax: 2));

        Assert.Equal(3, executor.CallCount);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_SecurityRejection_DoesNotRetryExecutor()
    {
        TextToSqlEngine engine = BuildEngine("DELETE FROM users");
        var executor = new FakeQueryExecutor();

        await Assert.ThrowsAsync<SqlSecurityException>(
            () => engine.GenerateAndExecuteManyAsync<object>(
                "delete all users", executor, llmFixRetryMax: 2));

        Assert.Equal(0, executor.CallCount);
    }

    // -------------------------------------------------------------------------
    // GenerateAndExecuteSingleAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAndExecuteSingleAsync_Success_ReturnsSingleItemAndSql()
    {
        TextToSqlEngine engine = BuildEngine("SELECT id, email FROM users WHERE id = 1");
        var executor = new FakeQueryExecutor();
        executor.ReturnsSingle(new { id = 1, email = "a@example.com" });

        var (data, sql) = await engine.GenerateAndExecuteSingleAsync<object>(
            "find user with id 1", executor, llmFixRetryMax: 0);

        Assert.Equal("SELECT id, email FROM users WHERE id = 1", sql);
        Assert.NotNull(data);
    }
}
