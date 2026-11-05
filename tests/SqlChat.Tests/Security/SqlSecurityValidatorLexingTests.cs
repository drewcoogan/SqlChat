using SqlChat.Service.Security;

namespace SqlChat.Tests.Security;

/// <summary>
/// Inputs where a naive scanner and the database disagree on where a literal or comment ends,
/// letting a mutating statement hide from the keyword scan.
/// </summary>
public sealed class SqlSecurityValidatorLexingTests
{
    [Theory]
    [InlineData("SELECT '--', 1\n; DROP TABLE orders")]                      // line-comment marker inside a literal
    [InlineData("SELECT '/*', 1 */ ; DROP TABLE orders; SELECT '")]          // block-comment marker inside a literal
    [InlineData("SELECT E'\\''; DELETE FROM users; --'")]                     // Postgres E-string backslash escape
    [InlineData("SELECT x FROM t WHERE E'\\'' = ' ; DROP TABLE t; --'")]      // same, read as SQL Server would
    [InlineData("SELECT 'C:\\'; DROP TABLE t; --'")]                          // standard_conforming_strings = off
    [InlineData("SELECT 1 /* /* */ '  */ ; DROP TABLE t; --'")]               // nested block comments
    [InlineData("SELECT 1 AS \"x'\", 2; DROP TABLE t; --'")]                  // quote inside a quoted identifier
    [InlineData("SELECT a[']'] FROM t; DROP TABLE t; --'")]                   // Postgres subscript holding a literal
    [InlineData("SELECT [it's], 1; DROP TABLE t; --']")]                      // SQL Server bracket identifier with quote
    [InlineData("SELECT a[1/*]  '  */ ; DROP TABLE t; --'")]                  // comment opener inside brackets
    [InlineData("SELECT $$'$$; DROP TABLE x; --'")]                           // Postgres dollar quoting
    [InlineData("SELECT $q$'$q$; DROP TABLE x; --'")]                         // tagged dollar quoting
    [InlineData("SELECT 'unterminated; DROP TABLE t")]                        // unterminated literal
    [InlineData("SELECT 1 /* unterminated; DROP TABLE t")]                    // unterminated block comment
    [InlineData("SELECT 1; LOCK TABLE t")]                                    // non-SELECT statement with no blocked keyword
    public void ValidateGeneratedSql_rejects_lexer_desync_bypass(string sql)
    {
        var v = new SqlSecurityValidator();

        var result = v.ValidateGeneratedSql(sql);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT '--not a comment' AS x")]
    [InlineData("SELECT '/* not a comment */' AS x")]
    [InlineData("SELECT 'it''s' AS x")]
    [InlineData("SELECT E'line\\nbreak' AS x")]
    [InlineData("SELECT * FROM t WHERE name LIKE '%\\_%'")]
    [InlineData("SELECT [Order Details].[Unit Price] FROM [Order Details]")]
    [InlineData("SELECT \"order-id\" FROM \"Orders\"")]
    [InlineData("SELECT tags[1] FROM posts")]
    [InlineData("SELECT price * 2 AS doubled, total$ FROM t")]
    [InlineData("SELECT 1 /* outer /* inner */ still comment */")]
    [InlineData("SELECT * FROM t WHERE code LIKE '[a-z]%'")]
    public void ValidateGeneratedSql_allows_legitimate_quoting_and_comments(string sql)
    {
        var v = new SqlSecurityValidator();

        var result = v.ValidateGeneratedSql(sql);

        Assert.True(result.IsValid, result.RejectionReason);
    }
}
