using System.Text.RegularExpressions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

/// <summary>
/// Dialect-agnostic checks: dangerous keywords, shape (SELECT / WITH … SELECT), and simple statement splitting.
/// </summary>
internal static class SqlHeuristicValidation
{
    private static readonly Regex DangerousKeywordPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|OPENROWSET|OPENDATASOURCE|BULK|SHUTDOWN|DBCC|BACKUP|RESTORE|COPY)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IntoPattern = new(
        @"\bINTO\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static SqlValidationResult Validate(string sqlWithoutComments, int maxSqlLength)
    {
        if (sqlWithoutComments.Length > maxSqlLength)
            return SqlValidationResult.Fail($"SQL exceeds maximum length ({maxSqlLength} characters).");

        var trimmed = sqlWithoutComments.Trim();
        if (trimmed.Length == 0)
            return SqlValidationResult.Fail("SQL is empty.");

        var forKeywordScan = ReplaceSingleQuotedStringLiterals(trimmed);

        if (DangerousKeywordPattern.IsMatch(forKeywordScan))
            return SqlValidationResult.Fail("SQL contains a disallowed keyword.");

        if (IntoPattern.IsMatch(forKeywordScan))
            return SqlValidationResult.Fail("SQL must not contain INTO (e.g. SELECT INTO).");

        if (!StartsWithSelectOrWith(trimmed.AsSpan()))
            return SqlValidationResult.Fail("SQL must begin with SELECT or WITH (after comments).");

        return ValidateStatements(trimmed);
    }

    private static bool StartsWithSelectOrWith(ReadOnlySpan<char> span)
    {
        var i = 0;
        while (i < span.Length && char.IsWhiteSpace(span[i]))
            i++;

        if (span.Length - i >= 6 && span.Slice(i, 6).Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (span.Length - i >= 4 && span.Slice(i, 4).Equals("WITH", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static SqlValidationResult ValidateStatements(string sql)
    {
        var segments = SplitSqlStatements(sql);
        if (segments.Count == 0)
            return SqlValidationResult.Fail("SQL contains no statements.");

        var hasNonEmpty = false;
        foreach (var segment in segments)
        {
            var s = segment.Trim();
            if (s.Length == 0)
                continue;

            hasNonEmpty = true;

            if (!StartsWithSelectOrWith(s.AsSpan()))
                return SqlValidationResult.Fail("Each statement must begin with SELECT or WITH.");

            var scan = ReplaceSingleQuotedStringLiterals(s);

            if (DangerousKeywordPattern.IsMatch(scan))
                return SqlValidationResult.Fail("SQL contains a disallowed keyword.");

            if (IntoPattern.IsMatch(scan))
                return SqlValidationResult.Fail("SQL must not contain INTO (e.g. SELECT INTO).");
        }

        if (!hasNonEmpty)
            return SqlValidationResult.Fail("SQL contains no statements.");

        return SqlValidationResult.Ok();
    }

    private static string ReplaceSingleQuotedStringLiterals(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                sb.Append(' ');
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            sb.Append(' ');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(' ');
                    i++;
                }

                continue;
            }

            sb.Append(sql[i]);
            i++;
        }

        return sb.ToString();
    }

    internal static List<string> SplitSqlStatements(string sql)
    {
        var parts = new List<string>();
        var start = 0;
        var inSingle = false;
        var inDouble = false;
        var bracketDepth = 0;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            if (!inDouble && c == '\'' && !inSingle)
            {
                inSingle = true;
                continue;
            }

            if (inSingle)
            {
                if (c == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }

                    inSingle = false;
                }

                continue;
            }

            if (!inSingle && c == '"' && bracketDepth == 0)
            {
                inDouble = !inDouble;
                continue;
            }

            if (!inSingle && !inDouble && c == '[')
            {
                bracketDepth++;
                continue;
            }

            if (!inSingle && !inDouble && c == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                continue;
            }

            if (!inSingle && !inDouble && bracketDepth == 0 && c == ';')
            {
                parts.Add(sql[start..i]);
                start = i + 1;
            }
        }

        parts.Add(sql[start..]);
        return parts;
    }

    internal static string StripComments(string sql)
    {
        var sb = new System.Text.StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\r' && sql[i] != '\n')
                    i++;
                continue;
            }

            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                i = i + 1 < sql.Length ? i + 2 : sql.Length;
                continue;
            }

            sb.Append(sql[i]);
            i++;
        }

        return sb.ToString();
    }
}

