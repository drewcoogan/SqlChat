namespace SqlChat.Domain.Abstractions;

public interface IQueryExecutor
{
    Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default);
    Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default);
}
