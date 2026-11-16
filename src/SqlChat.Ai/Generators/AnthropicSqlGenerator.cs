using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;

namespace SqlChat.Ai.Generators;

public sealed class AnthropicSqlGenerator : ISqlGenerator, IDisposable
{
    private const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    private readonly SqlChatOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AnthropicClient _client;

    public AnthropicSqlGenerator(SqlChatOptions options)
    {
        _options = options.Validate();

        string? apiKey = options.AnthropicApiKey.IsNotEmpty()
            ? options.AnthropicApiKey
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (apiKey.IsEmpty())
            throw new ArgumentException(
                $"Set {nameof(SqlChatOptions)}.{nameof(SqlChatOptions.AnthropicApiKey)} or the {ApiKeyEnvironmentVariable} environment variable.",
                nameof(options));

        var httpHandler = new HttpClientHandler(); // TODO: we can add parameters here for proxies into options if we want
        _httpClient = new HttpClient(httpHandler);
        _client = new AnthropicClient(new APIAuthentication(apiKey), _httpClient);
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken ct = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = _options.AnthropicModel,
            MaxTokens = _options.MaxOutputTokens,
            PromptCaching = PromptCacheType.FineGrained,
            System =
            [
                new SystemMessage(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
            ],
            Messages =
            [
                new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = naturalLanguage }]
                }
            ]
        }, ct).ConfigureAwait(false);

        // A response cut off at the token limit can still parse as SQL and silently answer a different question.
        if (string.Equals(response.StopReason, "max_tokens", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Anthropic stopped at the output token limit ({_options.MaxOutputTokens}); the SQL is incomplete. Increase {nameof(SqlChatOptions.MaxOutputTokens)}.");

        if (response.FirstMessage?.Text is { } text && text.IsNotEmpty())
            return SchemaPromptBuilder.ExtractSql(text);

        throw new InvalidOperationException("Anthropic returned no text content.");
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
    }
}
