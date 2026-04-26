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
}
