using System.Data;
using Dapper;
using Polly;

namespace SqlChat.Data.Internal;

internal static class DapperRetryExtensions
{
    private static readonly ResiliencePipeline DefaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential
        })
        .Build();

    public static async Task<IEnumerable<T>> QueryWithRetryAsync<T>(this IDbConnection connection, string sql,
        object? param = null, ResiliencePipeline? pipeline = null, CancellationToken ct = default)
    {
        var p = pipeline ?? DefaultPipeline;
        return await p.ExecuteAsync(
            async ct => await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    public static async Task<T?> QuerySingleOrDefaultWithRetryAsync<T>(this IDbConnection connection, string sql,
        object? param = null, ResiliencePipeline? pipeline = null, CancellationToken ct = default)
    {
        var p = pipeline ?? DefaultPipeline;
        return await p.ExecuteAsync(
            async ct => await connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }
}
