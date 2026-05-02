using System.Text;
using System.Text.RegularExpressions;
using SqlChat.Domain.Models;

namespace SqlChat.Data.Schema;

/// <summary>
/// Builds a <see cref="SchemaCatalog"/> from SQL DDL files without a live database connection.
/// Handles CREATE TABLE statements including inline and table-level PK/FK constraints.
/// </summary>
public static partial class DdlSchemaScanner
{
    public static SchemaCatalog ScanDirectory(string directoryPath, bool recursive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.sql", option);

        var tables = files
            .OrderBy(f => f)
            .SelectMany(f => ParseTables(File.ReadAllText(f)))
            .ToList();

        return new SchemaCatalog(tables);
    }

    public static SchemaCatalog ScanFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return new SchemaCatalog(ParseTables(File.ReadAllText(filePath)).ToList());
    }

    public static SchemaCatalog ScanSql(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new SchemaCatalog(ParseTables(sql).ToList());
    }

    internal static IEnumerable<TableDescriptor> ParseTables(string sql)
    {
        var stripped = StripComments(sql);

        foreach (Match m in CreateTableRegex().Matches(stripped))
        {
            var table = ParseCreateTable(m.Value);
            if (table is not null)
                yield return table;
        }
    }

    private static TableDescriptor? ParseCreateTable(string statement)
    {
        var nameMatch = TableNameRegex().Match(statement);
        if (!nameMatch.Success)
            return null;

        string? schema = nameMatch.Groups["schema"].Success ? Unquote(nameMatch.Groups["schema"].Value) : null;
        string name = Unquote(nameMatch.Groups["name"].Value);

        var bodyStart = statement.IndexOf('(');
        if (bodyStart < 0)
            return null;

        var body = ExtractParenBody(statement, bodyStart);
        if (body is null)
            return null;

        var pkCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fkMap = new Dictionary<string, ForeignKeyReference>(StringComparer.OrdinalIgnoreCase);
        var columnDefs = new List<string>();

        foreach (var part in SplitTopLevel(body))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            var upper = trimmed.ToUpperInvariant();

            if (upper.StartsWith("CONSTRAINT ") || upper.StartsWith("PRIMARY KEY ") || upper.StartsWith("FOREIGN KEY "))
                CollectConstraint(trimmed, pkCols, fkMap);
            else if (!upper.StartsWith("UNIQUE ") && !upper.StartsWith("INDEX ") && !upper.StartsWith("CHECK "))
                columnDefs.Add(trimmed);
        }

        var columns = columnDefs
            .Select(d => ParseColumnDef(d, pkCols, fkMap))
            .OfType<ColumnDescriptor>()
            .ToList();

        return columns.Count == 0 ? null : new TableDescriptor(name, schema, columns);
    }

    private static ColumnDescriptor? ParseColumnDef(
        string def,
        HashSet<string> tablePkCols,
        Dictionary<string, ForeignKeyReference> fkMap)
    {
        // First token is column name, second is type; rest are inline constraints
        var tokens = TokeniseColumnDef(def);
        if (tokens.Count < 2) return null;

        string colName = Unquote(tokens[0]);
        string storeType = BuildStoreType(tokens, out int consumed);

        var rest = string.Join(" ", tokens.Skip(consumed)).ToUpperInvariant();

        bool isNullable = !rest.Contains("NOT NULL");
        bool isPrimaryKey = tablePkCols.Contains(colName) || rest.Contains("PRIMARY KEY");

        if (isPrimaryKey) isNullable = false;

        ForeignKeyReference? fk = fkMap.TryGetValue(colName, out var mapped) ? mapped : ParseInlineFk(tokens, consumed);

        return ColumnDescriptor.Create(colName, storeType, isNullable: isNullable, isPrimaryKey: isPrimaryKey, foreignKey: fk);
    }

    // -------------------------------------------------------------------------
    // Constraint parsing
    // -------------------------------------------------------------------------

    private static void CollectConstraint(
        string def,
        HashSet<string> pkCols,
        Dictionary<string, ForeignKeyReference> fkMap)
    {
        var upper = def.ToUpperInvariant();

        // Strip leading CONSTRAINT name
        var working = def;
        if (upper.StartsWith("CONSTRAINT "))
        {
            var afterKeyword = def.Substring("CONSTRAINT ".Length).TrimStart();
            var spaceIdx = afterKeyword.IndexOf(' ');
            working = spaceIdx >= 0 ? afterKeyword.Substring(spaceIdx).TrimStart() : afterKeyword;
        }

        var workingUpper = working.ToUpperInvariant();

        if (workingUpper.StartsWith("PRIMARY KEY"))
        {
            var cols = ExtractColumnList(working);
            foreach (var c in cols) pkCols.Add(c);
        }
        else if (workingUpper.StartsWith("FOREIGN KEY"))
        {
            // FOREIGN KEY (col) REFERENCES table(col)
            var fkColsMatch = ParenContentRegex().Match(working);
            if (!fkColsMatch.Success) return;

            var fkCol = fkColsMatch.Value.Trim('(', ')').Trim();

            var refsIdx = workingUpper.IndexOf("REFERENCES", StringComparison.Ordinal);
            if (refsIdx < 0) return;

            var refsStr = working.Substring(refsIdx + "REFERENCES".Length).TrimStart();
            var (refTable, refSchema, refCol) = ParseRefTarget(refsStr);
            fkMap[fkCol] = new ForeignKeyReference(refTable, refSchema, refCol);
        }
    }

    // -------------------------------------------------------------------------
    // Inline FK (REFERENCES table(col) directly on the column line)
    // -------------------------------------------------------------------------

    private static ForeignKeyReference? ParseInlineFk(List<string> tokens, int startAfter)
    {
        for (int i = startAfter; i < tokens.Count; i++)
        {
            if (!tokens[i].Equals("REFERENCES", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= tokens.Count) break;

            var refStr = string.Join(" ", tokens.Skip(i + 1));
            var (table, schema, col) = ParseRefTarget(refStr);
            return new ForeignKeyReference(table, schema, col);
        }
        return null;
    }

    private static (string table, string? schema, string col) ParseRefTarget(string refStr)
    {
        // refStr examples: "customers(id)", "public.customers(id)", "customers (id)"
        var parenIdx = refStr.IndexOf('(');
        string tablePart = parenIdx >= 0 ? refStr.Substring(0, parenIdx).Trim() : refStr.Trim();
        string col = "id";

        if (parenIdx >= 0)
        {
            var closeIdx = refStr.IndexOf(')', parenIdx);
            col = closeIdx > parenIdx
                ? refStr.Substring(parenIdx + 1, closeIdx - parenIdx - 1).Trim()
                : col;
        }

        string? schema = null;
        string table;
        var dotIdx = tablePart.IndexOf('.');
        if (dotIdx >= 0)
        {
            schema = Unquote(tablePart.Substring(0, dotIdx).Trim());
            table = Unquote(tablePart.Substring(dotIdx + 1).Trim());
        }
        else
        {
            table = Unquote(tablePart);
        }

        return (table, schema, Unquote(col));
    }

    // -------------------------------------------------------------------------
    // Type extraction — handles type(precision) and multi-word types
    // -------------------------------------------------------------------------

    private static string BuildStoreType(List<string> tokens, out int consumed)
    {
        // tokens[0] = column name, tokens[1..] = type + constraints
        // Multi-word types: DOUBLE PRECISION, CHARACTER VARYING, TIMESTAMP WITH TIME ZONE, etc.
        // Type may include a parenthesised precision/scale: VARCHAR(255), NUMERIC(10,2)

        if (tokens.Count < 2)
        {
            consumed = 1;
            return "text";
        }

        var sb = new StringBuilder(tokens[1]);
        int i = 2;

        // Absorb precision parens if next token starts with '('
        if (i < tokens.Count && tokens[i].StartsWith('('))
        {
            sb.Append(tokens[i]);
            i++;
        }

        // Absorb known multi-word type continuations
        while (i < tokens.Count && IsTypeContinuation(tokens[i], tokens.Count > i + 1 ? tokens[i + 1] : null))
        {
            sb.Append(' ').Append(tokens[i]);
            i++;
        }

        consumed = i;
        return sb.ToString();
    }

    private static bool IsTypeContinuation(string token, string? next)
    {
        var upper = token.ToUpperInvariant();
        // DOUBLE PRECISION, CHARACTER VARYING, TIMESTAMP/TIME WITH TIME ZONE, WITHOUT TIME ZONE
        return upper is "PRECISION" or "VARYING" or "ZONE"
            || (upper == "WITH" && next?.ToUpperInvariant() == "TIME")
            || (upper == "WITHOUT" && next?.ToUpperInvariant() == "TIME")
            || (upper == "TIME" && next?.ToUpperInvariant() == "ZONE");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<string> TokeniseColumnDef(string def)
    {
        // Split on whitespace but keep parenthesised groups together
        var tokens = new List<string>();
        var current = new StringBuilder();
        int depth = 0;

        foreach (char c in def)
        {
            if (c == '(') { depth++; current.Append(c); }
            else if (c == ')') { depth--; current.Append(c); }
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        int depth = 0;
        var current = new StringBuilder();

        foreach (char c in body)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static List<string> ExtractColumnList(string constraint)
    {
        var m = ParenContentRegex().Match(constraint);
        if (!m.Success) return [];
        return m.Value.Trim('(', ')')
            .Split(',')
            .Select(c => Unquote(c.Trim()))
            .Where(c => c.Length > 0)
            .ToList();
    }

    private static string? ExtractParenBody(string sql, int openIdx)
    {
        int depth = 0;
        int start = openIdx + 1;
        for (int i = openIdx; i < sql.Length; i++)
        {
            if (sql[i] == '(') depth++;
            else if (sql[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return sql.Substring(start, i - start);
            }
        }
        return null;
    }

    private static string StripComments(string sql)
    {
        // Block comments
        sql = BlockCommentRegex().Replace(sql, " ");
        // Line comments
        sql = LineCommentRegex().Replace(sql, "");
        return sql;
    }

    private static string Unquote(string s) =>
        s.Trim('"', '`', '[', ']', '\'');

    // -------------------------------------------------------------------------
    // Compiled regexes
    // -------------------------------------------------------------------------

    [GeneratedRegex(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:""?\w+""?\.)?""?\w+""?\s*\((?:[^)(]|\((?:[^)(]|\((?:[^)(])*\))*\))*\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateTableRegex();

    [GeneratedRegex(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:(?<schema>""?\w+""?)\.)?(?<name>""?\w+""?)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableNameRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParenContentRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineCommentRegex();
}
