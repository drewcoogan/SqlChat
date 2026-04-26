namespace SqlChat.Domain.Options;

using SqlChat.Domain.Models;

public sealed class SqlChatOptions
{
    public string? ConnectionString { get; set; }
    public SqlDialect Dialect { get; set; } = SqlDialect.PostgreSql;

    public string? AnthropicApiKey { get; set; }
    public string AnthropicModel { get; set; } = "claude-opus-4-7";
    public int MaxOutputTokens { get; set; } = 2048;

    public int QueryRetryMax { get; set; } = 3;
    public int LlmFixRetryMax { get; set; } = 2;
}
