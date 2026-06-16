using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using SqlChat.Ai.Options;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Ai.Generators;

public sealed class OpenAiSqlGenerator : ISqlGenerator, IDisposable
{
    private readonly OpenAiSqlGeneratorOptions _options;
    private readonly OpenAIClient _client;

    public OpenAiSqlGenerator(OpenAiSqlGeneratorOptions options)
    {
        _options = options.Validate();
        _client = new OpenAIClient(_options.ApiKey!);
    }

    public async Task<string> GenerateAsync(
        string naturalLanguage,
        SchemaCatalog schema,
        CancellationToken ct = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);
        var chatClient = _client.GetChatClient(_options.Model);

        ClientResult<ChatCompletion> response = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(naturalLanguage)
            ],
            new ChatCompletionOptions
            {
                MaxOutputTokenCount = _options.MaxOutputTokens,
                Temperature = 0f
            },
            ct).ConfigureAwait(false);

        var text = response.Value.Content.Count > 0 ? response.Value.Content[0].Text : null;
        if (!string.IsNullOrWhiteSpace(text))
            return SchemaPromptBuilder.ExtractSql(text);

        throw new InvalidOperationException("OpenAI returned no text content.");
    }

    public void Dispose() { }  // OpenAIClient v2.x does not implement IDisposable
}
