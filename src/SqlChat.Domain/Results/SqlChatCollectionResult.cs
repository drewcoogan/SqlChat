namespace SqlChat.Domain.Results;

public record SqlChatCollectionResult<T> : SqlChatResult
{
    public IEnumerable<T> Data { get; init; } = [];

    public static SqlChatCollectionResult<T> Success(IEnumerable<T> data, string sql) =>
        new() { Data = data, Sql = sql, IsSuccess = true };

    public static SqlChatCollectionResult<T> Fail(string error) =>
        new() { IsSuccess = false, Error = error };

    public static SqlChatCollectionResult<T> Fail(string sql, string error) =>
        new() { Sql = sql, IsSuccess = false, Error = error };
}
