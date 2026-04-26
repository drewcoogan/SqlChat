namespace SqlChat.Domain.Results;

public record SqlChatResult
{
    public string Sql { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public bool IsFailed => !IsSuccess;
    public string? Error { get; init; }
}
