using System.Text;
using SqlChat.Domain.Models;

namespace SqlChat.Ai.Prompts;

internal static class SchemaPromptBuilder
{
    internal static string BuildSystemPrompt(SchemaCatalog schema, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert SQL query generator.");
        sb.AppendLine(dialect == SqlDialect.SqlServer ? "Database dialect: SQL Server (T-SQL)" : "Database dialect: PostgreSQL");
        sb.AppendLine();
        sb.AppendLine("Generate a valid SELECT query that answers the user's question using only the schema below.");
        sb.AppendLine("Use only SELECT statements. Never generate DML or DDL.");
        sb.AppendLine("Your entire response must be the SQL query and nothing else — no explanation, no markdown, no code fences, no words before or after the query.");
        sb.AppendLine("End the query with a semicolon.");
        sb.AppendLine();
        sb.AppendLine("## Schema");

        foreach (var table in schema.Tables)
        {
            sb.AppendLine();
            var tableRef = table.Schema is not null ? $"{table.Schema}.{table.Name}" : table.Name;
            sb.Append($"### {tableRef}");
            if (table.Description is not null)
                sb.Append($" — {table.Description}");
            sb.AppendLine();

            foreach (var col in table.Columns)
            {
                var nullable = col.IsNullable ? "NULL" : "NOT NULL";
                var pk = col.IsPrimaryKey ? " [PK]" : "";
                var fk = col.ForeignKey is not null
                    ? $" → {(col.ForeignKey.PrincipalSchema is not null ? col.ForeignKey.PrincipalSchema + "." : "")}{col.ForeignKey.PrincipalTable}.{col.ForeignKey.PrincipalColumn}"
                    : "";
                var desc = col.Description is not null ? $" // {col.Description}" : "";
                sb.AppendLine($"  - {col.Name}: {col.StoreType} {nullable}{pk}{fk}{desc}");
            }
        }

        if (schema.ExampleQueries is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Example Queries");
            sb.AppendLine("Use these as a reference for how questions should be answered:");
            foreach (var example in schema.ExampleQueries)
            {
                sb.AppendLine();
                sb.AppendLine($"Question: {example.Description}");
                sb.AppendLine($"SQL: {example.Sql}");
            }
        }

        return sb.ToString();
    }

    internal static string ExtractSql(string response)
    {
        var trimmed = response.Trim();

        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = trimmed.IndexOf('\n', fenceStart);
            if (contentStart >= 0)
            {
                var fenceEnd = trimmed.IndexOf("```", contentStart, StringComparison.Ordinal);
                if (fenceEnd >= 0)
                    return TrimToLastSemicolon(trimmed[contentStart..fenceEnd].Trim());
            }
        }

        // No fences — find the first line starting with SELECT or WITH and take
        // everything up to and including the last semicolon.
        var lines = trimmed.Split('\n');
        var sqlStart = Array.FindIndex(lines, l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        });

        if (sqlStart >= 0)
            return TrimToLastSemicolon(string.Join('\n', lines[sqlStart..]).Trim());

        return trimmed;
    }

    private static string TrimToLastSemicolon(string sql)
    {
        var lastSemi = sql.LastIndexOf(';');
        return lastSemi >= 0 ? sql[..(lastSemi + 1)] : sql;
    }
}
