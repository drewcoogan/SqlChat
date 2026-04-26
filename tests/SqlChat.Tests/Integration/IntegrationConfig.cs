using DotNetEnv;

namespace SqlChat.Tests.Integration;

internal static class IntegrationConfig
{
    static IntegrationConfig()
    {
        Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"),
            new LoadOptions(setEnvVars: true, clobberExistingVars: true, onlyExactPath: true));
    }

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("SQLCHAT_RUN_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("SQLCHAT_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "SQLCHAT_CONNECTION_STRING is not set. Check tests/SqlChat.Tests/.env.");

    public static string? AnthropicApiKey =>
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public static string AnthropicModel =>
        Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-opus-4-7";

    public static int QueryRetryMax =>
        int.TryParse(Environment.GetEnvironmentVariable("SQLCHAT_QUERY_RETRY_MAX"), out var v) ? v : 3;

    public static int LlmFixRetryMax =>
        int.TryParse(Environment.GetEnvironmentVariable("SQLCHAT_LLM_FIX_RETRY_MAX"), out var v) ? v : 2;
}
