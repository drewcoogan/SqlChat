using System.Text;
using System.Text.RegularExpressions;
using SqlChat.Domain.Models;

namespace SqlChat.Service.Security;

/// <summary>
/// Dialect-agnostic checks: dangerous keywords, shape (SELECT / WITH … SELECT), and simple statement splitting.
/// Lexing is fail-closed: any quoting or comment construct whose extent could differ between PostgreSQL and
/// SQL Server (or between Postgres string modes) is rejected rather than guessed at, so the scanner never sees
/// less of the SQL than the database will execute.
/// </summary>
internal static class SqlHeuristicValidation
{
    private static readonly Regex DangerousKeywordPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|EXEC|EXECUTE|GRANT|REVOKE|OPENROWSET|OPENDATASOURCE|BULK|SHUTDOWN|DBCC|BACKUP|RESTORE|COPY)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IntoPattern = new(
        @"\bINTO\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static SqlValidationResult Validate(string sql, int maxSqlLength)
    {
        if (sql.Length > maxSqlLength)
            return SqlValidationResult.Fail($"SQL exceeds maximum length ({maxSqlLength} characters).");

        if (TrySanitize(sql, out var sanitized) is { } lexFailure)
            return lexFailure;

        var trimmed = sanitized.Trim();
        if (trimmed.Length == 0)
            return SqlValidationResult.Fail("SQL is empty.");

        if (DangerousKeywordPattern.IsMatch(trimmed))
            return SqlValidationResult.Fail("SQL contains a disallowed keyword.");

        if (IntoPattern.IsMatch(trimmed))
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

    // Input is already sanitized (comments removed, string literals blanked), so keyword scans have run on the
    // whole text; each statement only needs its shape checked.
    private static SqlValidationResult ValidateStatements(string sanitizedSql)
    {
        var hasNonEmpty = false;
        foreach (var segment in SplitSqlStatements(sanitizedSql))
        {
            var s = segment.Trim();
            if (s.Length == 0)
                continue;

            hasNonEmpty = true;

            if (!StartsWithSelectOrWith(s.AsSpan()))
                return SqlValidationResult.Fail("Each statement must begin with SELECT or WITH.");
        }

        if (!hasNonEmpty)
            return SqlValidationResult.Fail("SQL contains no statements.");

        return SqlValidationResult.Ok();
    }

    // Splits sanitized SQL on ';' outside double-quoted identifiers. Semicolons inside [brackets] still split:
    // Postgres reads brackets as array subscripts, so honouring them could hide a statement boundary.
    private static List<string> SplitSqlStatements(string sanitizedSql)
    {
        var parts = new List<string>();
        var start = 0;
        var inDouble = false;

        for (var i = 0; i < sanitizedSql.Length; i++)
        {
            var c = sanitizedSql[i];

            if (c == '"')
                inDouble = !inDouble;
            else if (c == ';' && !inDouble)
            {
                parts.Add(sanitizedSql[start..i]);
                start = i + 1;
            }
        }

        parts.Add(sanitizedSql[start..]);
        return parts;
    }

    /// <summary>
    /// Single left-to-right pass that removes comments and replaces single-quoted literals with a space,
    /// leaving identifiers intact. Returns a failure when the SQL contains a construct that is unterminated
    /// or that PostgreSQL and SQL Server would tokenize differently.
    /// </summary>
    private static SqlValidationResult? TrySanitize(string sql, out string sanitized)
    {
        sanitized = string.Empty;
        var sb = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (c == '-' && next == '-')
            {
                while (i < sql.Length && sql[i] != '\r' && sql[i] != '\n')
                    i++;
                sb.Append(' ');
                continue;
            }

            if (c == '/' && next == '*')
            {
                if (!TrySkipBlockComment(sql, ref i))
                    return SqlValidationResult.Fail("SQL contains an unterminated block comment.");
                sb.Append(' ');
                continue;
            }

            if (c == '\'')
            {
                // Postgres E'' strings (and servers with standard_conforming_strings off) treat backslash as an
                // escape; SQL Server and standard Postgres strings do not. Reject when the two readings disagree.
                var standardEnd = FindQuotedEnd(sql, i, '\'', backslashEscapes: false);
                var backslashEnd = FindQuotedEnd(sql, i, '\'', backslashEscapes: true);
                if (standardEnd != backslashEnd)
                    return SqlValidationResult.Fail("SQL contains a string literal with a backslash-escaped quote, which is ambiguous across dialects.");
                if (standardEnd < 0)
                    return SqlValidationResult.Fail("SQL contains an unterminated string literal.");

                i = standardEnd;
                sb.Append(' ');
                continue;
            }

            if (c == '"')
            {
                var end = FindQuotedEnd(sql, i, '"', backslashEscapes: false);
                if (end < 0)
                    return SqlValidationResult.Fail("SQL contains an unterminated quoted identifier.");

                sb.Append(sql, i, end - i);
                i = end;
                continue;
            }

            if (c == '[')
            {
                // SQL Server bracket identifier vs Postgres array subscript: only safe when the contents
                // contain nothing either dialect could read as the start of a literal, identifier or comment.
                var end = FindQuotedEnd(sql, i, ']', backslashEscapes: false);
                if (end < 0)
                    return SqlValidationResult.Fail("SQL contains an unterminated '[' bracket.");

                var content = sql.AsSpan(i + 1, end - i - 2);
                if (content.IndexOfAny('\'', '"', '$') >= 0
                    || content.Contains("--", StringComparison.Ordinal)
                    || content.Contains("/*", StringComparison.Ordinal))
                    return SqlValidationResult.Fail("SQL contains quotes or comment markers inside [brackets], which is ambiguous across dialects.");

                sb.Append(sql, i, end - i);
                i = end;
                continue;
            }

            if (c == '$' && IsDollarQuoteOpener(sql, i))
                return SqlValidationResult.Fail("SQL contains a dollar-quoted string, which is not allowed.");

            sb.Append(c);
            i++;
        }

        sanitized = sb.ToString();
        return null;
    }

    // Returns the index just past the closing delimiter, or -1 when unterminated. A doubled closing
    // delimiter ('' / "" / ]]) is an escaped character, as in both Postgres and SQL Server.
    private static int FindQuotedEnd(string sql, int openIndex, char close, bool backslashEscapes)
    {
        var j = openIndex + 1;
        while (j < sql.Length)
        {
            if (backslashEscapes && sql[j] == '\\')
            {
                j += 2;
                continue;
            }

            if (sql[j] == close)
            {
                if (j + 1 < sql.Length && sql[j + 1] == close)
                {
                    j += 2;
                    continue;
                }

                return j + 1;
            }

            j++;
        }

        return -1;
    }

    // Block comments nest in both Postgres and SQL Server, so track depth to match where the server ends them.
    private static bool TrySkipBlockComment(string sql, ref int i)
    {
        var depth = 0;
        while (i + 1 < sql.Length)
        {
            if (sql[i] == '/' && sql[i + 1] == '*')
            {
                depth++;
                i += 2;
                continue;
            }

            if (sql[i] == '*' && sql[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                    return true;
                continue;
            }

            i++;
        }

        return false;
    }

    // Postgres dollar quotes: $$ or $tag$ not preceded by an identifier character ($1 parameters don't match).
    private static bool IsDollarQuoteOpener(string sql, int i)
    {
        if (i > 0 && (char.IsLetterOrDigit(sql[i - 1]) || sql[i - 1] is '_' or '$'))
            return false;

        var j = i + 1;
        if (j < sql.Length && (char.IsLetter(sql[j]) || sql[j] == '_'))
        {
            while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
                j++;
        }

        return j < sql.Length && sql[j] == '$';
    }
}
