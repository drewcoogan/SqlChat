using System.Text;
using System.Text.RegularExpressions;
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

    // "WITH" must look like a CTE head ("WITH name AS", "WITH RECURSIVE name (cols) AS") so that prose such
    // as "With that in mind, ..." is not taken for the start of the query.
    private static readonly Regex QueryStartPattern = new(
        @"^[ \t]*(?:SELECT\b|WITH\s+(?:RECURSIVE\s+)?[\w""\[\]]+\s*(?:\([^)]*\)\s*)?AS\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled);

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

        // No fences — start at the first line that begins a query and take everything up to and including
        // the last semicolon.
        Match queryStart = QueryStartPattern.Match(trimmed);
        if (queryStart.Success)
            return TrimToLastSemicolon(trimmed[queryStart.Index..].Trim());

        return trimmed;
    }

    // Cuts trailing prose after the final statement terminator. Semicolons inside quoted text are not
    // terminators, so "SELECT 'a;b' FROM t" is left whole.
    private static string TrimToLastSemicolon(string sql)
    {
        var lastSemi = -1;
        var quote = '\0';

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '\'' or '"')
                quote = c;
            else if (c == ';')
                lastSemi = i;
        }

        return lastSemi >= 0 ? sql[..(lastSemi + 1)] : sql;
    }
}
