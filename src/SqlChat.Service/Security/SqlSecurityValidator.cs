using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

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
        return SqlHeuristicValidation.Validate(stripped, _options.MaxSqlLength);
    }
}

