using Anthropic;
using SqlChat.Ai.Options;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Ai.Generators;

public sealed class AnthropicSqlGenerator : ISqlGenerator, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicApi _api;
    private readonly SqlChatAiOptions _options;

    public AnthropicSqlGenerator(SqlChatAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var apiKey = options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("Anthropic API key is not configured. Set AnthropicApiKey or the ANTHROPIC_API_KEY environment variable.");

        _httpClient = new HttpClient();
        _api = new AnthropicApi(_httpClient, new Uri("https://api.anthropic.com"));
        _api.AuthorizeUsingApiKey(apiKey);
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var response = await _api.CreateMessageAsync(new CreateMessageRequest
        {
            Model = _options.AnthropicModel,
            MaxTokens = _options.MaxOutputTokens,
            System = systemPrompt,
            Messages = [naturalLanguage.AsUserMessage()],
        }, cancellationToken).ConfigureAwait(false);

        string? text;
        if (response.Content.IsValue2)
        {
            var block = response.Content.Value2.FirstOrDefault(b => b.IsText);
            text = block.IsText ? block.Text.Text : null;
        }
        else
        {
            text = response.Content.Value1;
        }

        return SchemaPromptBuilder.ExtractSql(
            text ?? throw new InvalidOperationException("Anthropic returned no text content."));
    }

    public void Dispose() => _httpClient.Dispose();
}
