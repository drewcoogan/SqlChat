namespace SqlChat.Domain.Results;

public record SqlChatResult<T> : SqlChatResult
{
    public T? Data { get; init; }

    public static SqlChatResult<T> Success(T? data, string sql) =>
        new() { Data = data, Sql = sql, IsSuccess = true };

    public new static SqlChatResult<T> Fail(string error) =>
        new() { IsSuccess = false, Error = error };

    public static SqlChatResult<T> Fail(string sql, string error) =>
        new() { Sql = sql, IsSuccess = false, Error = error };
}
