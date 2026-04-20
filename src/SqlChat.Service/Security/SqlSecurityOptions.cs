namespace SqlChat.Service.Security;

/// <summary>
/// Options for <see cref="SqlSecurityValidator"/>.
/// </summary>
public sealed class SqlSecurityOptions
{
    /// <summary>
    /// Maximum length of generated SQL accepted for validation (defense against abuse).
    /// </summary>
    public int MaxSqlLength { get; init; } = 256_000;

    /// <summary>
    /// When true, parsed SQL is also validated with Microsoft SQL Server T-SQL ScriptDom AST:
    /// only <c>SELECT</c> statements are allowed in each batch. Non–T-SQL dialects will fail this step;
    /// keep this false if you generate PostgreSQL or other dialects.
    /// </summary>
    public bool UseTsqlScriptDomAstValidation { get; init; }
}

