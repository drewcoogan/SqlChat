using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;

namespace SqlChat.Ai.Generators;

public sealed class AnthropicSqlGenerator : ISqlGenerator, IDisposable
{
    private readonly SqlChatOptions _options;
    private readonly HttpClient _httpClient;
    private readonly AnthropicClient _client;

    public AnthropicSqlGenerator(SqlChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AnthropicApiKey, nameof(options.AnthropicApiKey));
        _options = options;

        var httpHandler = new HttpClientHandler(); // TODO: we can add parameters here for proxies into options if we want
        _httpClient = new HttpClient(httpHandler);
        _client = new AnthropicClient(new APIAuthentication(options.AnthropicApiKey), _httpClient);
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = _options.AnthropicModel,
            MaxTokens = _options.MaxOutputTokens,
            Temperature = 0,
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
        }, cancellationToken).ConfigureAwait(false);


        if (response.FirstMessage?.Text is { } text && !string.IsNullOrWhiteSpace(text))
            return SchemaPromptBuilder.ExtractSql(text);

        throw new InvalidOperationException("Anthropic returned no text content.");
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
    }
}
