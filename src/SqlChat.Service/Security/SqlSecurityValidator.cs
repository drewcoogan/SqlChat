using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

/// <summary>
/// Default implementation: heuristic validation for any dialect, optional strict T-SQL AST checks via ScriptDom.
/// Intended to run on <b>generated SQL</b> right before it is returned or executed.
/// </summary>
public sealed class SqlSecurityValidator : ISqlSecurityValidator
{
    private readonly SqlSecurityOptions _options;

    public SqlSecurityValidator(SqlSecurityOptions? options = null)
    {
        _options = options ?? new SqlSecurityOptions();
    }

    public SqlValidationResult ValidateGeneratedSql(string sql)
    {
        if (sql is null)
            return SqlValidationResult.Fail("SQL is null.");

        var stripped = SqlHeuristicValidation.StripComments(sql);
        var heuristic = SqlHeuristicValidation.Validate(stripped, _options.MaxSqlLength);
        if (!heuristic.IsValid)
            return heuristic;

        if (!_options.UseTsqlScriptDomAstValidation)
            return SqlValidationResult.Ok();

        return TsqlScriptDomValidation.Validate(stripped);
    }
}

