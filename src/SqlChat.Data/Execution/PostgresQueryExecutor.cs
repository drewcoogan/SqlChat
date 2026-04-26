using Npgsql;
using Polly;
using Polly.Retry;
using SqlChat.Data.Internal;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Options;

namespace SqlChat.Data.Execution;

public sealed class PostgresQueryExecutor : IQueryExecutor
{
    private readonly string _connectionString;
    private readonly ResiliencePipeline _pipeline;

    public PostgresQueryExecutor(SqlChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(options.ConnectionString));

        _connectionString = options.ConnectionString;
        _pipeline = BuildPipeline(options.QueryRetryMax);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QueryWithRetryAsync<T>(sql, pipeline: _pipeline, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultWithRetryAsync<T>(sql, pipeline: _pipeline, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

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
