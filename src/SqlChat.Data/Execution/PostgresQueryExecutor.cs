using Dapper;
using Npgsql;
using Polly;
using Polly.Retry;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Options;

namespace SqlChat.Data.Execution;

/// <summary>
/// Executes generated SQL inside a <c>READ ONLY</c> transaction that is always rolled back, so Postgres itself
/// rejects any write that slips past <c>SqlSecurityValidator</c>. This does not block side-effecting functions
/// such as <c>pg_terminate_backend()</c>; connect with a SELECT-only role for that (see README "Safety").
/// </summary>
public sealed class PostgresQueryExecutor(SqlChatOptions options) : IQueryExecutor
{
    private const string SetReadOnly = "SET TRANSACTION READ ONLY";

    private readonly string _connectionString = options
        .Validate(nameof(options))
        .ConnectionString
        .Validate(v => v.IsNotEmpty())!;
    private readonly ResiliencePipeline _pipeline = BuildPipeline(options.QueryRetryMax);

    public Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default) =>
        ExecuteReadOnlyAsync(
            (connection, transaction, ct) => connection.QueryAsync<T>(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct)),
            cancellationToken);

    public Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default) =>
        ExecuteReadOnlyAsync(
            (connection, transaction, ct) => connection.QuerySingleOrDefaultAsync<T?>(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct)),
            cancellationToken);

    // The retry wraps the whole connect → begin → query unit: a transient failure leaves the transaction
    // unusable, so retrying only the query on the same connection could never succeed.
    private async Task<TResult> ExecuteReadOnlyAsync<TResult>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken) =>
        await _pipeline.ExecuteAsync(async ct =>
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(SetReadOnly, transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);

            // Dapper buffers results by default, so rows are fully read before the rollback.
            var result = await query(connection, transaction, ct).ConfigureAwait(false);
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return result;
        }, cancellationToken).ConfigureAwait(false);

    private static ResiliencePipeline BuildPipeline(int maxRetryAttempts) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(ex => ex.IsTransient)
            })
            .Build();
}
