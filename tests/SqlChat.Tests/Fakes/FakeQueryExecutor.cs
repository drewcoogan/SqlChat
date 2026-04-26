using SqlChat.Domain.Abstractions;

namespace SqlChat.Tests.Fakes;

/// <summary>
/// Queue-based fake executor. Each call to QueryAsync or QuerySingleAsync dequeues
/// the next configured action — either returning data or throwing an exception.
/// Enqueue actions with Returns() or Throws() before the test runs.
/// </summary>
internal sealed class FakeQueryExecutor : IQueryExecutor
{
    private readonly Queue<Func<Type, object>> _queue = new();

    public int CallCount { get; private set; }

    public void Returns<T>(IEnumerable<T> data)
    {
        var snapshot = data.ToList();
        _queue.Enqueue(_ => snapshot);
    }

    public void ReturnsSingle<T>(T? item) =>
        _queue.Enqueue(_ => item!);

    public void Throws(Exception ex) =>
        _queue.Enqueue(_ => throw ex);

    public Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = (IEnumerable<T>)Dequeue(typeof(T));
        return Task.FromResult(result);
    }

    public Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = (T?)Dequeue(typeof(T));
        return Task.FromResult(result);
    }

    private object Dequeue(Type type)
    {
        if (!_queue.TryDequeue(out var action))
            throw new InvalidOperationException(
                $"FakeQueryExecutor has no more queued responses. Configure more calls to Returns() or Throws() for type {type.Name}.");
        return action(type);
    }
}
