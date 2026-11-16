using SqlChat.Service.Security;

namespace SqlChat.Tests.Security;

/// <summary>
/// T-SQL needs no ';' between statements, so a batch can begin with SELECT and still carry a second,
/// side-effecting statement. These must be rejected by keyword because the statement splitter cannot see them.
/// </summary>
public sealed class SqlSecurityValidatorTSqlBatchTests
{
    [Theory]
    [InlineData("SELECT 1 DENY SELECT ON dbo.orders TO public")]
    [InlineData("SELECT 1 KILL 53")]
    [InlineData("SELECT 1 WAITFOR DELAY '00:10:00'")]
    [InlineData("SELECT 1 DISABLE TRIGGER ALL ON dbo.orders")]
    [InlineData("SELECT 1 ENABLE TRIGGER ALL ON dbo.orders")]
    [InlineData("SELECT * FROM OPENQUERY(lnk, 'DELETE FROM t')")]
    [InlineData("SELECT 1 USE master")]
    [InlineData("SELECT 1 SET IDENTITY_INSERT dbo.t ON")]
    [InlineData("SELECT 1 DECLARE @x int")]
    [InlineData("SELECT 1 BEGIN TRANSACTION")]
    [InlineData("SELECT 1 WHILE 1 = 1 SELECT 1")]
    [InlineData("SELECT 1 UPDATETEXT t.c @p 0 NULL 'x'")]
    [InlineData("SELECT 1 WRITETEXT t.c @p 'x'")]
    [InlineData("SELECT 1 RECEIVE * FROM dbo.queue")]
    [InlineData("SELECT 1 RECONFIGURE")]
    [InlineData("SELECT 1 CHECKPOINT")]
    [InlineData("SELECT 1 REVERT")]
    [InlineData("SELECT 1 ADD SENSITIVITY CLASSIFICATION TO dbo.t.c WITH (LABEL = 'x')")]
    public void ValidateGeneratedSql_SelectFollowedByUnterminatedTSqlStatement_ReturnsInvalid(string sql)
    {
        var validator = new SqlSecurityValidator();

        var result = validator.ValidateGeneratedSql(sql);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("SELECT id FROM orders ORDER BY id OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
    [InlineData("SELECT CASE WHEN total > 0 THEN 1 ELSE 0 END AS has_total FROM orders")]
    [InlineData("SELECT updated_at, dataset, reset_count, added_on, user_id, is_enabled FROM t")]
    [InlineData("SELECT [open], [close] FROM prices")]
    [InlineData("SELECT region, SUM(total) FROM orders GROUP BY GROUPING SETS ((region), ())")]
    public void ValidateGeneratedSql_OrdinarySelectNearNewKeywords_ReturnsValid(string sql)
    {
        var validator = new SqlSecurityValidator();

        var result = validator.ValidateGeneratedSql(sql);

        Assert.True(result.IsValid, result.RejectionReason);
    }

    [Theory]
    [InlineData("SELECT \"update\", \"set\" FROM t")]
    [InlineData("SELECT o.\"delete\" FROM \"into\" o")]
    public void ValidateGeneratedSql_KeywordAsDoubleQuotedIdentifier_ReturnsValid(string sql)
    {
        var validator = new SqlSecurityValidator();

        var result = validator.ValidateGeneratedSql(sql);

        Assert.True(result.IsValid, result.RejectionReason);
    }

    [Theory]
    [InlineData("SELECT 1 INTO \"t\"")]
    [InlineData("SELECT \"a\" FROM t DELETE FROM \"t\"")]
    [InlineData("SELECT \"a\"\"b\" FROM t; DROP TABLE \"t\"")]
    public void ValidateGeneratedSql_KeywordOutsideDoubleQuotedIdentifier_ReturnsInvalid(string sql)
    {
        var validator = new SqlSecurityValidator();

        var result = validator.ValidateGeneratedSql(sql);

        Assert.False(result.IsValid);
    }
}
