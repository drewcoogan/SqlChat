using SqlChat.Ai.Prompts;

namespace SqlChat.Tests.Generation;

public sealed class ExtractSqlTests
{
    [Fact]
    public void ExtractSql_SemicolonInsideStringLiteral_KeepsWholeQuery()
    {
        string sql = SchemaPromptBuilder.ExtractSql("SELECT 'a;b' AS v FROM t");

        Assert.Equal("SELECT 'a;b' AS v FROM t", sql);
    }

    [Fact]
    public void ExtractSql_TrailingProseAfterTerminator_IsCut()
    {
        string sql = SchemaPromptBuilder.ExtractSql("SELECT 'a;b' AS v FROM t;\nThat's the query you asked for.");

        Assert.Equal("SELECT 'a;b' AS v FROM t;", sql);
    }

    [Fact]
    public void ExtractSql_ProseLineStartingWithWith_IsNotTakenAsQueryStart()
    {
        string sql = SchemaPromptBuilder.ExtractSql("With that in mind, here is the query:\nSELECT id FROM users;");

        Assert.Equal("SELECT id FROM users;", sql);
    }

    [Theory]
    [InlineData("Here you go:\nWITH recent AS (SELECT id FROM orders)\nSELECT * FROM recent;", "WITH recent AS (SELECT id FROM orders)\nSELECT * FROM recent;")]
    [InlineData("WITH RECURSIVE t(n) AS (SELECT 1) SELECT n FROM t;", "WITH RECURSIVE t(n) AS (SELECT 1) SELECT n FROM t;")]
    [InlineData("WITH\n  recent AS (SELECT 1 AS id)\nSELECT id FROM recent;", "WITH\n  recent AS (SELECT 1 AS id)\nSELECT id FROM recent;")]
    public void ExtractSql_CommonTableExpression_StartsAtWith(string response, string expected)
    {
        Assert.Equal(expected, SchemaPromptBuilder.ExtractSql(response));
    }
}
