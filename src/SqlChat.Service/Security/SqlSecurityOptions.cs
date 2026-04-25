namespace SqlChat.Service.Security;

public sealed class SqlSecurityOptions
{
    public int MaxSqlLength { get; init; } = 256_000;
}

