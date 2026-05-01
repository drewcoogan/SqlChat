using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using SqlChat.Data.Internal;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Options;

namespace SqlChat.Data.Execution;

public sealed class SqlServerQueryExecutor(SqlChatOptions options) : IQueryExecutor
{
    private readonly string _connectionString = options
        .Validate(nameof(options))
        .ConnectionString
        .Validate(v => v.IsNotEmpty())!;
    private readonly ResiliencePipeline _pipeline = BuildPipeline(options.QueryRetryMax);

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QueryWithRetryAsync<T>(sql, pipeline: _pipeline, ct: cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultWithRetryAsync<T>(sql, pipeline: _pipeline, ct: cancellationToken).ConfigureAwait(false);
    }

    private static ResiliencePipeline BuildPipeline(int maxRetryAttempts) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(ex => ex.IsTransient)
            })
            .Build();
}
