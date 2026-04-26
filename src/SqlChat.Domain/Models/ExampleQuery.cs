namespace SqlChat.Domain.Models;

/// <summary>
/// A natural language question paired with the SQL query that correctly answers it.
/// Included in the LLM system prompt as few-shot examples to improve generation quality.
/// </summary>
public sealed record ExampleQuery(string Description, string Sql);
