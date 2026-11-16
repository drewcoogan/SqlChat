using SqlChat.Domain.Builders;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;
using SqlChat.Service.Engine;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Engine;

public sealed class TextToSqlEngineRetryTests
{
    private static readonly SchemaCatalog TestSchema = new SchemaCatalogBuilder()
        .AddTable("users", t => t.AddColumn("id", "int", primaryKey: true))
        .Build();

    private static TextToSqlEngine BuildEngine(SequencedFakeSqlGenerator generator) =>
        new(new ManualSchemaProvider(TestSchema), generator);

    [Fact]
    public async Task GenerateAndExecuteManyAsync_NonRetryableFailure_StopsWithoutAskingLlmAgain()
    {
        var generator = new SequencedFakeSqlGenerator("SELECT id FROM users");
        var executor = new FakeQueryExecutor();
        executor.Throws(new NonRetryableQueryException("password authentication failed", new InvalidOperationException()));

        SqlExecutionFailedException ex = await Assert.ThrowsAsync<SqlExecutionFailedException>(
            () => BuildEngine(generator).GenerateAndExecuteManyAsync<object>("find users", executor, llmFixRetryMax: 2));

        Assert.Equal(1, generator.CallCount);
        Assert.Equal(1, executor.CallCount);
        Assert.Contains("password authentication failed", ex.Message);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_Cancelled_StopsWithoutAskingLlmAgain()
    {
        var generator = new SequencedFakeSqlGenerator("SELECT id FROM users");
        var executor = new FakeQueryExecutor();
        executor.Throws(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => BuildEngine(generator).GenerateAndExecuteManyAsync<object>("find users", executor, llmFixRetryMax: 2));

        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_SecurityRejectionThenValidSql_RetriesAndReturnsData()
    {
        var generator = new SequencedFakeSqlGenerator("DELETE FROM users", "SELECT id FROM users");
        var executor = new FakeQueryExecutor();
        executor.Returns(new[] { new { id = 1 } });

        var (data, sql) = await BuildEngine(generator)
            .GenerateAndExecuteManyAsync<object>("find users", executor, llmFixRetryMax: 1);

        Assert.Equal(2, generator.CallCount);
        Assert.Equal(1, executor.CallCount);
        Assert.Equal("SELECT id FROM users", sql);
        Assert.Single(data);
    }

    [Fact]
    public async Task GenerateAndExecuteManyAsync_SecurityRejectionOnEveryAttempt_ThrowsAndNeverExecutes()
    {
        var generator = new SequencedFakeSqlGenerator("DELETE FROM users");
        var executor = new FakeQueryExecutor();

        await Assert.ThrowsAsync<SqlSecurityException>(
            () => BuildEngine(generator).GenerateAndExecuteManyAsync<object>("delete users", executor, llmFixRetryMax: 2));

        Assert.Equal(3, generator.CallCount);
        Assert.Equal(0, executor.CallCount);
    }
}
