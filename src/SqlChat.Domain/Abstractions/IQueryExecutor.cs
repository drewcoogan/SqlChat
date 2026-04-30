namespace SqlChat.Domain.Abstractions;

public interface IQueryExecutor
{
    Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken ct = default);
    Task<T?> QuerySingleAsync<T>(string sql, CancellationToken ct = default);
}
