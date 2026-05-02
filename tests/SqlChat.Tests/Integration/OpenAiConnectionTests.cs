using OpenAI;
using OpenAI.Chat;

namespace SqlChat.Tests.Integration;

public sealed class OpenAiConnectionTests
{
    [IntegrationFact]
    public async Task Ping_Succeeds()
    {
        var apiKey = IntegrationConfig.OpenAiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set or empty. Check tests/SqlChat.Tests/.env.");

        var client = new OpenAIClient(apiKey);
        var chatClient = client.GetChatClient("gpt-4o-mini");

        var response = await chatClient.CompleteChatAsync(
            [new UserChatMessage("ping")],
            new ChatCompletionOptions { MaxOutputTokenCount = 1 },
            CancellationToken.None).ConfigureAwait(false);

        Assert.NotEqual(default, response.Value.FinishReason);
    }
}
