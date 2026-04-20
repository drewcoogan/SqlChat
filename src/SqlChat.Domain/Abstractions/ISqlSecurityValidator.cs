namespace SqlChat.Domain.Abstractions;

using SqlChat.Domain.Models;

/// <summary>
/// Validates generated SQL before it is returned or executed. This library is scoped to read-only
/// <c>SELECT</c> queries; implementations should reject DDL/DML and other unsafe patterns.
/// </summary>
/// <remarks>
/// Validation runs on the final generated SQL (late in the pipeline). Natural-language input can
/// be screened separately; the concrete string is the most reliable place to enforce policy.
/// </remarks>
public interface ISqlSecurityValidator
{
    /// <summary>
    /// Validates a SQL string produced by the text-to-SQL pipeline.
    /// </summary>
    SqlValidationResult ValidateGeneratedSql(string sql);
}

