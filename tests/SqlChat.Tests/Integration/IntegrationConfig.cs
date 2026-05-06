using DotNetEnv;

namespace SqlChat.Tests.Integration;

internal static class IntegrationConfig
{
    private static readonly LoadOptions _envLoadOptions = new(setEnvVars: true, clobberExistingVars: true, onlyExactPath: true);

    static IntegrationConfig() =>
        Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"), _envLoadOptions);

    public static bool IsEnabled =>
        bool.TryParse(Environment.GetEnvironmentVariable("SQLCHAT_RUN_INTEGRATION_TESTS"), out var v) && v;

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("SQLCHAT_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("SQLCHAT_POSTGRES_CONNECTION_STRING is not set. Check tests/SqlChat.Tests/.env.");

    public static string? AnthropicApiKey =>
        Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public static string AnthropicModel =>
        Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5-20251001";

    public static int AnthropicMaxOutputTokens =>
        int.TryParse(Environment.GetEnvironmentVariable("ANTHROPIC_MAX_OUTPUT_TOKENS"), out var v) ? v : 2048;

    public static string? OpenAiApiKey =>
        Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public static string OpenAiModel =>
        Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

    public static int QueryRetryMax =>
        int.TryParse(Environment.GetEnvironmentVariable("SQLCHAT_QUERY_RETRY_MAX"), out var v) ? v : 3;

    public static int LlmFixRetryMax =>
        int.TryParse(Environment.GetEnvironmentVariable("SQLCHAT_LLM_FIX_RETRY_MAX"), out var v) ? v : 2;

    public static bool IsSqlServerEnabled =>
        IsEnabled && Environment.GetEnvironmentVariable("SQLCHAT_SQLSERVER_CONNECTION_STRING") is not null;

    public static bool IsAnthropicEnabled =>
        IsEnabled && !string.IsNullOrWhiteSpace(AnthropicApiKey);

    public static bool IsOpenAiEnabled =>
        IsEnabled && !string.IsNullOrWhiteSpace(OpenAiApiKey);

    public static string SqlServerConnectionString =>
        Environment.GetEnvironmentVariable("SQLCHAT_SQLSERVER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("SQLCHAT_SQLSERVER_CONNECTION_STRING is not set. Check tests/SqlChat.Tests/.env.");
}
