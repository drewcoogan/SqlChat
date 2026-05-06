namespace SqlChat.Tests.Integration;

[AttributeUsage(AttributeTargets.Method)]
[Trait("Category", "Integration")]
public sealed class SqlServerIntegrationFactAttribute : FactAttribute
{
    public SqlServerIntegrationFactAttribute()
    {
        if (!IntegrationConfig.IsSqlServerEnabled)
            Skip = "Set SQLCHAT_RUN_INTEGRATION_TESTS=true and SQLCHAT_SQLSERVER_CONNECTION_STRING to run SQL Server integration tests.";
        else if (!IntegrationConfig.IsAnthropicEnabled)
            Skip = "Set SQLCHAT_RUN_INTEGRATION_TESTS=true and a valid ANTHROPIC_API_KEY to run SQL Server integration tests.";
    }
}
