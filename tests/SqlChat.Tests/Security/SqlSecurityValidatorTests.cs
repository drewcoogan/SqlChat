using SqlChat.Client;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;
using SqlChat.Service.Engine;
using SqlChat.Service.Security;

namespace SqlChat.Tests.Security;

public sealed class SqlSecurityValidatorTests
{
    [Fact]
    public void ValidateGeneratedSql_allows_simple_select()
    {
        var v = new SqlSecurityValidator();
        var result = v.ValidateGeneratedSql("SELECT 1");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_update()
    {
        var v = new SqlSecurityValidator();
        var result = v.ValidateGeneratedSql("UPDATE Users SET IsAdmin = 1");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_allows_keyword_text_inside_string_literal()
    {
        var v = new SqlSecurityValidator();
        var result = v.ValidateGeneratedSql("SELECT 'DROP TABLE Users' AS msg");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_multiple_statements_when_any_is_not_select()
    {
        var v = new SqlSecurityValidator();
        var result = v.ValidateGeneratedSql("SELECT 1; UPDATE Users SET IsAdmin = 1;");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_select_into()
    {
        var v = new SqlSecurityValidator();
        var result = v.ValidateGeneratedSql("SELECT 1 INTO #t");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateGeneratedSqlOrThrow_throws_when_invalid()
    {
        var engine = new TextToSqlEngine(new ManualSchemaProvider(SchemaCatalog.Empty));
        Assert.Throws<SqlSecurityException>(() => engine.ValidateGeneratedSqlOrThrow("DELETE FROM Users"));
    }

    [Fact]
    public void Builder_can_enable_tsql_ast_validation()
    {
        var client = new SqlChatBuilder()
            .UseSchema(SchemaCatalog.Empty)
            .UseSqlSecurity(new SqlSecurityOptions { UseTsqlScriptDomAstValidation = true })
            .Build();

        Assert.True(client.ValidateGeneratedSql("SELECT 1").IsValid);
        Assert.False(client.ValidateGeneratedSql("SELECT 1 INTO #t").IsValid);
    }
}

