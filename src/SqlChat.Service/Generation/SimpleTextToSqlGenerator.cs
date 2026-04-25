using System.Text;
using System.Text.RegularExpressions;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Generation;

/// <summary>
/// Minimal, rules-based generator used as a bootstrap before introducing an LLM.
/// Scope: single-table SELECT, optional COUNT(*), simple equality predicates.
/// </summary>
public sealed class SimpleTextToSqlGenerator : ISqlGenerator
{
    private static readonly Regex CountPattern = new(@"\b(count|how many)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default) =>
        Task.FromResult(Generate(naturalLanguage, schema));

    internal static string Generate(string naturalLanguage, SchemaCatalog schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        ArgumentNullException.ThrowIfNull(schema);

        var table = PickTable(naturalLanguage, schema);
        if (table is null)
            throw new InvalidOperationException("Unable to infer a table from the prompt.");

        var isCount = CountPattern.IsMatch(naturalLanguage);
        var where = BuildWhereClause(naturalLanguage, table);

        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append(isCount ? "COUNT(*)" : "*");
        sb.Append(" FROM ");
        sb.Append(FormatTableName(table));

        if (where is not null)
        {
            sb.Append(" WHERE ");
            sb.Append(where);
        }

        return sb.ToString();
    }

    private static TableDescriptor? PickTable(string naturalLanguage, SchemaCatalog schema)
    {
        if (schema.Tables.Count == 1)
            return schema.Tables[0];

        var nl = Normalize(naturalLanguage);

        TableDescriptor? best = null;
        var bestScore = 0;
        foreach (var t in schema.Tables)
        {
            var score = 0;
            var tableName = Normalize(t.Name);
            if (nl.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                score += 10;

            if (!nl.Contains(tableName, StringComparison.OrdinalIgnoreCase) && tableName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                var singular = tableName[..^1];
                if (singular.Length > 2 && nl.Contains(singular, StringComparison.OrdinalIgnoreCase))
                    score += 8;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static string? BuildWhereClause(string naturalLanguage, TableDescriptor table)
    {
        foreach (var c in table.Columns)
        {
            var name = c.Name;
            var pattern = @$"(?i)\b{Regex.Escape(name)}\b\s*(=|is)\s*(?<value>[^\r\n;]+)";
            var m = Regex.Match(naturalLanguage, pattern, RegexOptions.CultureInvariant);
            if (!m.Success)
                continue;

            var raw = m.Groups["value"].Value.Trim();
            raw = TrimTrailingPunctuation(raw);
            if (raw.Length == 0)
                continue;

            return $"{name} = {FormatValue(raw)}";
        }

        return null;
    }

    private static string FormatTableName(TableDescriptor table) =>
        table.Schema is null ? table.Name : $"{table.Schema}.{table.Name}";

    private static string FormatValue(string raw)
    {
        if (long.TryParse(raw, out _))
            return raw;

        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            return raw;

        raw = raw.Trim();
        if (raw.Length >= 2 && ((raw.StartsWith('\'') && raw.EndsWith('\'')) || (raw.StartsWith('"') && raw.EndsWith('"'))))
            raw = raw[1..^1];

        return $"'{raw.Replace("'", "''")}'";
    }

    private static string TrimTrailingPunctuation(string value) =>
        value.TrimEnd('.', ',', ')');

    private static string Normalize(string s) =>
        s.Trim().Replace("_", " ").Replace("-", " ");
}
