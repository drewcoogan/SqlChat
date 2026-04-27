using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Newtonsoft.Json;

namespace SqlChat.Tests.Integration;

public sealed class AnthropicConnectionTests
{
    [IntegrationFact]
    public async Task Ping_Succeeds()
    {
        var apiKey = IntegrationConfig.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY is not set or empty. Check tests/SqlChat.Tests/.env.");

        using var http = new HttpClient();
        using var client = new AnthropicClient(new APIAuthentication(apiKey), http);

        var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = "claude-haiku-4-5-20251001",
            MaxTokens = 1,
            Messages =
            [
                new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent { Text = "ping" }]
                }
            ]
        }, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(JsonConvert.SerializeObject(response));

        Assert.NotNull(response.StopReason);
    }
}
