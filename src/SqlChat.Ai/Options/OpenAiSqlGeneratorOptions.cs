using SqlChat.Domain.Models;

namespace SqlChat.Ai.Options;

public sealed class OpenAiSqlGeneratorOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public SqlDialect Dialect { get; set; } = SqlDialect.PostgreSql;
    public int MaxOutputTokens { get; set; } = 2048;

    internal OpenAiSqlGeneratorOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey, nameof(ApiKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxOutputTokens, nameof(MaxOutputTokens));
        return this;
    }
}
