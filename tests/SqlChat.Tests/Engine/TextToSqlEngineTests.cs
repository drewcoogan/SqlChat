using Microsoft.Extensions.DependencyInjection;
using SqlChat.Client;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;
using SqlChat.Domain.Results;
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

        SqlExecutionFailedException ex = await Assert.ThrowsAsync<SqlExecutionFailedException>(
            () => engine.GenerateAndExecuteManyAsync<object>(
                "find all users", executor, llmFixRetryMax: 2));

        Assert.Equal(3, executor.CallCount);
        Assert.Equal("SELECT id FROM users", ex.Sql);
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

    // -------------------------------------------------------------------------
    // Custom validator cannot replace the built-in checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateSqlAsync_PermissiveCustomValidator_StillRejectsDml()
    {
        var engine = new TextToSqlEngine(
            new ManualSchemaProvider(TestSchema), new FakeSqlGenerator("DROP TABLE users"), new AllowAllValidator());

        await Assert.ThrowsAsync<SqlSecurityException>(
            () => engine.GenerateSqlAsync("delete everything"));
    }

    [Fact]
    public void ValidateGeneratedSql_StricterCustomValidator_RejectsSelectTheBuiltInAllows()
    {
        var engine = new TextToSqlEngine(
            new ManualSchemaProvider(TestSchema), new FakeSqlGenerator("SELECT 1"), new RejectAllValidator());

        SqlValidationResult result = engine.ValidateGeneratedSql("SELECT id FROM users");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task SqlChatBuilder_PermissiveCustomValidator_PreviewStillFails()
    {
        ISqlChatClient client = new SqlChatBuilder()
            .UseSchema(TestSchema)
            .UseGenerator(new FakeSqlGenerator("DROP TABLE users"))
            .UseSqlSecurity(new AllowAllValidator())
            .Build();

        SqlChatResult result = await client.PreviewSqlAsync("delete everything");

        Assert.False(result.IsSuccess);
    }

    // -------------------------------------------------------------------------
    // Failure results carry the SQL and the database error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_AllRetriesExhausted_ResultHasLastSqlAndDatabaseError()
    {
        var generator = new SequencedFakeSqlGenerator("SELECT bad FROM users", "SELECT worse FROM users");
        var executor = new FakeQueryExecutor();
        executor.Throws(new InvalidOperationException("column \"bad\" does not exist"));
        executor.Throws(new InvalidOperationException("column \"worse\" does not exist"));
        var client = BuildClient(generator, executor, llmFixRetryMax: 1);

        SqlChatCollectionResult<object> result = await client.QueryAsync<object>("find all users");

        Assert.False(result.IsSuccess);
        Assert.Equal("SELECT worse FROM users", result.Sql);
        Assert.Contains("2 attempt(s)", result.Error);
        Assert.Contains("column \"worse\" does not exist", result.Error);
    }

    [Fact]
    public async Task QuerySingleAsync_ExecutionFails_ResultHasSqlAndDatabaseError()
    {
        var executor = new FakeQueryExecutor();
        executor.Throws(new InvalidOperationException("relation missing"));
        var client = BuildClient(new FakeSqlGenerator("SELECT id FROM users"), executor, llmFixRetryMax: 0);

        SqlChatResult<object> result = await client.QuerySingleAsync<object>("find a user");

        Assert.False(result.IsSuccess);
        Assert.Equal("SELECT id FROM users", result.Sql);
        Assert.Contains("relation missing", result.Error);
    }

    [Fact]
    public async Task QueryAsync_SecurityRejection_ResultHasNoSql()
    {
        var client = BuildClient(new FakeSqlGenerator("DELETE FROM users"), new FakeQueryExecutor(), llmFixRetryMax: 0);

        SqlChatCollectionResult<object> result = await client.QueryAsync<object>("delete all users");

        Assert.False(result.IsSuccess);
        Assert.Equal(string.Empty, result.Sql);
    }

    // SqlChatClient's constructor is internal to SqlChat.Client, so resolve it through DI.
    private static ISqlChatClient BuildClient(ISqlGenerator generator, IQueryExecutor executor, int llmFixRetryMax)
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseGenerator(generator).WithOptions(o => o.LlmFixRetryMax = llmFixRetryMax);
        services.AddSingleton<ISchemaProvider>(new ManualSchemaProvider(TestSchema));
        services.AddSingleton(executor);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISqlChatClient>();
    }

    private sealed class AllowAllValidator : ISqlSecurityValidator
    {
        public SqlValidationResult ValidateGeneratedSql(string sql) => SqlValidationResult.Ok();
    }

    private sealed class RejectAllValidator : ISqlSecurityValidator
    {
        public SqlValidationResult ValidateGeneratedSql(string sql) => SqlValidationResult.Fail("rejected by custom rule");
    }
}
