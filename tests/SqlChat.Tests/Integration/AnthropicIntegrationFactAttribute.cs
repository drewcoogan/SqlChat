namespace SqlChat.Tests.Integration;

[AttributeUsage(AttributeTargets.Method)]
[Trait("Category", "Integration")]
public sealed class AnthropicIntegrationFactAttribute : FactAttribute
{
    public AnthropicIntegrationFactAttribute()
    {
        if (!IntegrationConfig.IsAnthropicEnabled)
            Skip = "Set SQLCHAT_RUN_INTEGRATION_TESTS=true and a valid ANTHROPIC_API_KEY to run Anthropic integration tests.";
    }
}
