using Npgsql;
using SqlChat.Data.Execution;
using SqlChat.Domain.Options;

namespace SqlChat.Tests.Integration;

/// <summary>
/// Calls the executor directly — bypassing <c>SqlSecurityValidator</c> — to prove Postgres itself
/// refuses writes inside SqlChat's read-only transaction.
/// </summary>
public sealed class PostgresReadOnlyExecutionTests
{
    private const string ReadOnlySqlState = "25006"; // read_only_sql_transaction

    private static PostgresQueryExecutor CreateExecutor() =>
        new(new SqlChatOptions { ConnectionString = IntegrationConfig.ConnectionString, QueryRetryMax = 0 });

    [IntegrationFact]
    public async Task QueryAsync_Select_ReturnsRows()
    {
        var executor = CreateExecutor();

        var rows = await executor.QueryAsync<int>("SELECT 1 UNION ALL SELECT 2");

        Assert.Equal(new[] { 1, 2 }, rows);
    }

    [IntegrationFact]
    public async Task QueryAsync_CreateTable_RejectedByDatabase()
    {
        var executor = CreateExecutor();

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => executor.QueryAsync<int>("CREATE TABLE sqlchat_readonly_probe (id int)"));

        Assert.Equal(ReadOnlySqlState, ex.SqlState);
    }

    [IntegrationFact]
    public async Task QueryAsync_SelectThenDelete_RejectedByDatabase()
    {
        var executor = CreateExecutor();

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => executor.QueryAsync<int>("SELECT 1; DELETE FROM pg_catalog.pg_class WHERE false"));

        Assert.Equal(ReadOnlySqlState, ex.SqlState);
    }

    [IntegrationFact]
    public async Task QueryAsync_SetConfigReadWrite_CannotEscapeReadOnly()
    {
        var executor = CreateExecutor();

        await Assert.ThrowsAsync<PostgresException>(
            () => executor.QueryAsync<string>("SELECT set_config('transaction_read_only', 'off', true)"));
    }
}
