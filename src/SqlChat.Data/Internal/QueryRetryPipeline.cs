using Polly;
using Polly.Retry;

namespace SqlChat.Data.Internal;

internal static class QueryRetryPipeline
{
    /// <summary>
    /// Builds a pipeline that retries transient <typeparamref name="TException"/> failures with jittered
    /// exponential backoff. Zero retries yields a pass-through pipeline (Polly rejects a retry count of 0).
    /// </summary>
    public static ResiliencePipeline Build<TException>(int maxRetryAttempts, Func<TException, bool> isTransient)
        where TException : Exception
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetryAttempts);

        if (maxRetryAttempts == 0)
            return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle(isTransient)
            })
            .Build();
    }
}
