using Npgsql;
using Polly;
using Polly.Retry;
using SqlChat.Data.Internal;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Options;

namespace SqlChat.Data.Execution;

public sealed class PostgresQueryExecutor(SqlChatOptions options) : IQueryExecutor
{
    private readonly string _connectionString = options
        .Validate(nameof(options))
        .ConnectionString
        .Validate(v => v.IsNotEmpty())!;
    private readonly ResiliencePipeline _pipeline = BuildPipeline(options.QueryRetryMax);

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var res = await connection.QueryWithRetryAsync<T>(sql, pipeline: _pipeline, ct: cancellationToken).ConfigureAwait(false);
        return res;
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var res = await connection.QuerySingleOrDefaultWithRetryAsync<T>(sql, pipeline: _pipeline, ct: cancellationToken).ConfigureAwait(false);
        return res;
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
