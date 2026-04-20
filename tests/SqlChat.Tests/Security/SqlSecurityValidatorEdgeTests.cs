using SqlChat.Service.Security;

namespace SqlChat.Tests.Security;

public sealed class SqlSecurityValidatorEdgeTests
{
    [Fact]
    public void ValidateGeneratedSql_allows_leading_comments()
    {
        var v = new SqlSecurityValidator();
        var sql = """
                  -- leading comment
                  /* another comment */
                  SELECT 1
                  """;

        Assert.True(v.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_allows_cte()
    {
        var v = new SqlSecurityValidator();
        var sql = """
                  WITH x AS (SELECT 1 AS a)
                  SELECT a FROM x
                  """;

        Assert.True(v.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_allows_semicolon_inside_string_literal()
    {
        var v = new SqlSecurityValidator();
        var sql = "SELECT ';' AS semi; SELECT 1;";
        Assert.True(v.ValidateGeneratedSql(sql).IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_exec_even_if_prefixed_by_select_statement()
    {
        var v = new SqlSecurityValidator();
        var sql = "SELECT 1; EXEC('SELECT 1');";
        Assert.False(v.ValidateGeneratedSql(sql).IsValid);
    }
}

