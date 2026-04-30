using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

public sealed class SqlSecurityValidator(SqlSecurityOptions? options = null) : ISqlSecurityValidator
{
    private readonly SqlSecurityOptions _options = options ?? new SqlSecurityOptions();

    public SqlValidationResult ValidateGeneratedSql(string sql)
    {
        if (sql is null)
            return SqlValidationResult.Fail("SQL is null.");

        var stripped = SqlHeuristicValidation.StripComments(sql);
        return SqlHeuristicValidation.Validate(stripped, _options.MaxSqlLength);
    }
}

