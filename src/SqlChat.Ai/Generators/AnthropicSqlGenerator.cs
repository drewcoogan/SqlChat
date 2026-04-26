using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;

namespace SqlChat.Ai.Generators;

public sealed class AnthropicSqlGenerator : ISqlGenerator, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicClient _client;
    private readonly SqlChatOptions _options;

    public AnthropicSqlGenerator(SqlChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var apiKey = options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Anthropic API key is not configured. Set AnthropicApiKey or the ANTHROPIC_API_KEY environment variable.");

        _httpClient = new HttpClient();
        _client = new AnthropicClient(new APIAuthentication(apiKey), _httpClient);
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = _options.AnthropicModel,
            MaxTokens = _options.MaxOutputTokens,
            SystemMessage = systemPrompt,
            Messages =
            [
                new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = naturalLanguage }]
                }
            ]
        }, null, cancellationToken).ConfigureAwait(false);

        var text = response.FirstMessage?.Text
            ?? throw new InvalidOperationException("Anthropic returned no text content.");

        return SchemaPromptBuilder.ExtractSql(text);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
    }
}
