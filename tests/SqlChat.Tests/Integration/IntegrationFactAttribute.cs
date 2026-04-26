namespace SqlChat.Tests.Integration;

/// <summary>
/// Marks a test as an integration test. The test is skipped unless
/// SQLCHAT_RUN_INTEGRATION_TESTS=true is set in the environment or .env file.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[Trait("Category", "Integration")]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationConfig.IsEnabled)
            Skip = "Set SQLCHAT_RUN_INTEGRATION_TESTS=true to run integration tests.";
    }
}
