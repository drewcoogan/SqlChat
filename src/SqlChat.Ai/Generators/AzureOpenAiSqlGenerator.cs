using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using SqlChat.Ai.Options;
using SqlChat.Ai.Prompts;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Ai.Generators;

public sealed class AzureOpenAiSqlGenerator : ISqlGenerator
{
    private readonly ChatClient _chatClient;
    private readonly SqlChatAiOptions _options;

    public AzureOpenAiSqlGenerator(SqlChatAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var endpoint = options.AzureOpenAiEndpoint ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("Azure OpenAI endpoint is not configured.");
        var apiKey = options.AzureOpenAiApiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Azure OpenAI API key is not configured.");
        var deployment = options.AzureOpenAiDeployment ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
            ?? throw new InvalidOperationException("Azure OpenAI deployment name is not configured.");

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _chatClient = azureClient.GetChatClient(deployment);
    }

    public async Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        var systemPrompt = SchemaPromptBuilder.BuildSystemPrompt(schema, _options.Dialect);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(naturalLanguage),
        };

        var completion = await _chatClient.CompleteChatAsync(
            messages,
            new ChatCompletionOptions { MaxOutputTokenCount = _options.MaxOutputTokens },
            cancellationToken).ConfigureAwait(false);

        var text = completion.Value.Content[0].Text
            ?? throw new InvalidOperationException("Azure OpenAI returned no text content.");

        return SchemaPromptBuilder.ExtractSql(text);
    }
}
