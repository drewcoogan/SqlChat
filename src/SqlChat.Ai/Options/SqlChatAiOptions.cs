using SqlChat.Domain.Models;

namespace SqlChat.Ai.Options;

public sealed class SqlChatAiOptions
{
    public SqlDialect Dialect { get; set; } = SqlDialect.PostgreSql;
    public int MaxOutputTokens { get; set; } = 2048;

    // Anthropic
    public string? AnthropicApiKey { get; set; }
    public string AnthropicModel { get; set; } = "claude-opus-4-7";

    // Azure OpenAI
    public string? AzureOpenAiEndpoint { get; set; }
    public string? AzureOpenAiApiKey { get; set; }
    public string? AzureOpenAiDeployment { get; set; }
}
