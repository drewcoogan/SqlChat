namespace SqlChat.Domain.Results;

public record SqlChatResult
{
    public string Sql { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }

    public static SqlChatResult Success(string sql) =>
        new() { Sql = sql, IsSuccess = true };

    public static SqlChatResult Fail(string error) =>
        new() { IsSuccess = false, Error = error };
}
