using SqlChat.Ai.Prompts;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Generation;

public sealed class SchemaPromptBuilderTests
{
    private static SchemaCatalog SimpleSchema() => new SchemaCatalogBuilder()
        .AddTable("users", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("email", "varchar"))
        .Build();

    [Fact]
    public void BuildSystemPrompt_NoExamples_DoesNotContainExampleSection()
    {
        string prompt = SchemaPromptBuilder.BuildSystemPrompt(SimpleSchema(), SqlDialect.PostgreSql);

        Assert.DoesNotContain("## Example Queries", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithExamples_RendersExampleSection()
    {
        SchemaCatalog schema = SimpleSchema() with
        {
            ExampleQueries =
            [
                new ExampleQuery("Show all users", "SELECT * FROM users"),
                new ExampleQuery("Find user by email", "SELECT * FROM users WHERE email = 'a@b.com'")
            ]
        };

        string prompt = SchemaPromptBuilder.BuildSystemPrompt(schema, SqlDialect.PostgreSql);

        Assert.Contains("## Example Queries", prompt);
        Assert.Contains("Show all users", prompt);
        Assert.Contains("SELECT * FROM users", prompt);
        Assert.Contains("Find user by email", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithExamples_EachExampleHasQuestionAndSqlLabels()
    {
        SchemaCatalog schema = SimpleSchema() with
        {
            ExampleQueries = [new ExampleQuery("Show all users", "SELECT id, email FROM users")]
        };

        string prompt = SchemaPromptBuilder.BuildSystemPrompt(schema, SqlDialect.PostgreSql);

        Assert.Contains("Question: Show all users", prompt);
        Assert.Contains("SQL: SELECT id, email FROM users", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsSchemaSection()
    {
        string prompt = SchemaPromptBuilder.BuildSystemPrompt(SimpleSchema(), SqlDialect.PostgreSql);

        Assert.Contains("## Schema", prompt);
        Assert.Contains("users", prompt);
        Assert.Contains("email", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_InstructsSemicolonTermination()
    {
        string prompt = SchemaPromptBuilder.BuildSystemPrompt(SimpleSchema(), SqlDialect.PostgreSql);

        Assert.Contains("semicolon", prompt);
    }

    // -------------------------------------------------------------------------
    // ExtractSql
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractSql_PlainQuery_ReturnsTrimmed()
    {
        string result = SchemaPromptBuilder.ExtractSql("SELECT * FROM users;");

        Assert.Equal("SELECT * FROM users;", result);
    }

    [Fact]
    public void ExtractSql_FencedSql_StripsMarkdown()
    {
        string result = SchemaPromptBuilder.ExtractSql("```sql\nSELECT * FROM users;\n```");

        Assert.Equal("SELECT * FROM users;", result);
    }

    [Fact]
    public void ExtractSql_FencedSqlWithTrailingText_TrimsToLastSemicolon()
    {
        string result = SchemaPromptBuilder.ExtractSql("```sql\nSELECT * FROM users;\nThis query returns all users.\n```");

        Assert.Equal("SELECT * FROM users;", result);
    }

    [Fact]
    public void ExtractSql_ProseBeforeQuery_ReturnsFromSelectLine()
    {
        string response = "Here is the SQL query:\n\nSELECT * FROM users;";

        string result = SchemaPromptBuilder.ExtractSql(response);

        Assert.Equal("SELECT * FROM users;", result);
    }

    [Fact]
    public void ExtractSql_ProseBeforeAndAfterQuery_TrimsToLastSemicolon()
    {
        string response = "Here is the SQL:\n\nSELECT * FROM users;\n\nThis returns all users.";

        string result = SchemaPromptBuilder.ExtractSql(response);

        Assert.Equal("SELECT * FROM users;", result);
    }

    [Fact]
    public void ExtractSql_CteWithMultipleSelectKeywords_TrimsToLastSemicolon()
    {
        string response = "WITH summary AS (\n    SELECT id FROM users\n)\nSELECT * FROM summary;";

        string result = SchemaPromptBuilder.ExtractSql(response);

        Assert.Equal(response, result);
    }

    [Fact]
    public void ExtractSql_WithKeyword_RecognisedAsQueryStart()
    {
        string response = "Sure!\n\nWITH cte AS (SELECT 1)\nSELECT * FROM cte;";

        string result = SchemaPromptBuilder.ExtractSql(response);

        Assert.Equal("WITH cte AS (SELECT 1)\nSELECT * FROM cte;", result);
    }

    [Fact]
    public void ExtractSql_NoSemicolon_ReturnsTrimmedContent()
    {
        string result = SchemaPromptBuilder.ExtractSql("SELECT * FROM users");

        Assert.Equal("SELECT * FROM users", result);
    }
}
