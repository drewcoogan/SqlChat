namespace SqlChat.Tests.Integration;

[AttributeUsage(AttributeTargets.Method)]
[Trait("Category", "Integration")]
public sealed class OpenAiIntegrationFactAttribute : FactAttribute
{
    public OpenAiIntegrationFactAttribute()
    {
        if (!IntegrationConfig.IsOpenAiEnabled)
            Skip = "Set SQLCHAT_RUN_INTEGRATION_TESTS=true and a valid OPENAI_API_KEY to run OpenAI integration tests.";
    }
}
