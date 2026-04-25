using Anthropic;
using Anthropic.Models.Messages;
using SqlChat.Ai.Options;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Ai.Generators;

public sealed class AnthropicSqlGenerator : ISqlGenerator
{
    private readonly AnthropicClient _client;
    private readonly SqlChatAiOptions _options;

    public AnthropicSqlGenerator(SqlChatAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _client = new AnthropicClient
        {
            ApiKey = options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("Anthropic API key is not configured. Set AnthropicApiKey or the ANTHROPIC_API_KEY environment variable."),
        };
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _options.AnthropicModel,
            MaxTokens = _options.MaxOutputTokens,
            System = new List<TextBlockParam>
            {
                new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
            },
            Messages =
            [
                new() { Role = Role.User, Content = naturalLanguage },
            ],
        }, cancellationToken).ConfigureAwait(false);

        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Anthropic returned no text content.");

        return SchemaPromptBuilder.ExtractSql(text);
    }
}
