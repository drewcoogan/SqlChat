using SqlChat.Domain.Abstractions;
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
        Assert.True(v.ValidateGeneratedSql("SELECT 1").IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_update()
    {
        var v = new SqlSecurityValidator();
        Assert.False(v.ValidateGeneratedSql("UPDATE Users SET IsAdmin = 1").IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_allows_keyword_text_inside_string_literal()
    {
        var v = new SqlSecurityValidator();
        Assert.True(v.ValidateGeneratedSql("SELECT 'DROP TABLE Users' AS msg").IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_multiple_statements_when_any_is_not_select()
    {
        var v = new SqlSecurityValidator();
        Assert.False(v.ValidateGeneratedSql("SELECT 1; UPDATE Users SET IsAdmin = 1;").IsValid);
    }

    [Fact]
    public void ValidateGeneratedSql_rejects_select_into()
    {
        var v = new SqlSecurityValidator();
        Assert.False(v.ValidateGeneratedSql("SELECT 1 INTO #t").IsValid);
    }

    [Fact]
    public void ValidateGeneratedSqlOrThrow_throws_when_invalid()
    {
        var noOpGenerator = new NoOpGenerator();
        var engine = new TextToSqlEngine(new ManualSchemaProvider(SchemaCatalog.Empty), noOpGenerator);
        Assert.Throws<SqlSecurityException>(() => engine.ValidateGeneratedSqlOrThrow("DELETE FROM Users"));
    }

    private sealed class NoOpGenerator : ISqlGenerator
    {
        public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default) =>
            Task.FromResult("SELECT 1");
    }
}

