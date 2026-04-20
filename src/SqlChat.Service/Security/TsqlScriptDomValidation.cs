using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

/// <summary>
/// Optional T-SQL AST validation (Microsoft SQL Server dialect only).
/// </summary>
internal static class TsqlScriptDomValidation
{
    internal static SqlValidationResult Validate(string sql)
    {
        var parser = new TSql180Parser(false);
        var fragment = parser.Parse(new StringReader(sql), out var errors);

        if (errors is { Count: > 0 })
        {
            var first = errors[0];
            return SqlValidationResult.Fail(
                $"SQL is not valid T-SQL (parse error at line {first.Line}, column {first.Column}).");
        }

        if (fragment is not TSqlScript script)
            return SqlValidationResult.Fail("Unexpected T-SQL parse result.");

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (statement is not SelectStatement select)
                    return SqlValidationResult.Fail("Only SELECT statements are allowed (T-SQL AST).");

                if (select.Into != null)
                    return SqlValidationResult.Fail("SELECT INTO is not allowed.");
            }
        }

        return SqlValidationResult.Ok();
    }
}

