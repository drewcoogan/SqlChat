using SqlChat.Domain.Models;
using SqlChat.Service.Engine;

namespace SqlChat.Client;

/// <summary>
/// Application-facing entry point for SqlChat.
/// </summary>
public sealed class SqlChatClient(TextToSqlEngine engine)
{
    private readonly TextToSqlEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken cancellationToken = default) =>
        _engine.GenerateSqlAsync(naturalLanguage, cancellationToken);

    /// <inheritdoc cref="TextToSqlEngine.ValidateGeneratedSql" />
    public SqlValidationResult ValidateGeneratedSql(string sql) =>
        _engine.ValidateGeneratedSql(sql);

    /// <inheritdoc cref="TextToSqlEngine.ValidateGeneratedSqlOrThrow" />
    public void ValidateGeneratedSqlOrThrow(string sql) =>
        _engine.ValidateGeneratedSqlOrThrow(sql);
}
