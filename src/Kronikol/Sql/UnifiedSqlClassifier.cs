using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Sql;

/// <summary>
/// Unified SQL operation classifier shared across all database tracking extensions.
/// Supports SQL Server, PostgreSQL, MySQL, SQLite, Oracle, and Spanner dialects.
/// </summary>
public static partial class UnifiedSqlClassifier
{
    // Strips Spanner statement hints like @{PDML_MAX_PARALLELISM=10}
    [GeneratedRegex(@"^\s*@\{[^}]*\}\s*", RegexOptions.Compiled)]
    private static partial Regex SpannerHintRegex();

    // Strips SET statements before real DML (e.g. SET NOCOUNT ON;\n)
    [GeneratedRegex(@"^\s*SET\s[^;]*;\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SetPrefixRegex();

    // Strips CTEs: WITH name AS (...) — handles nested parentheses
    [GeneratedRegex(@"^\s*WITH\s+.+?\)\s+(?=SELECT|INSERT|UPDATE|DELETE|MERGE)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex CtePrefixRegex();

    // Matches the first keyword after prefix stripping
    [GeneratedRegex(@"^\s*(?<keyword>SELECT|INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|CALL|EXPLAIN|COPY|LOAD|BULK|TRUNCATE|BEGIN|COMMIT|ROLLBACK|CREATE|ALTER|DROP|OPTIMIZE|RENAME|ATTACH|DETACH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FirstKeywordRegex();

    // ClickHouse lightweight mutations: ALTER TABLE [db.]t [ON CLUSTER c] UPDATE|DELETE ...
    [GeneratedRegex(@"^\s*ALTER\s+TABLE\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?" + @"(?:\s+ON\s+CLUSTER\s+\S+)?\s+(?<mutation>UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AlterMutationRegex();

    // OPTIMIZE TABLE name (ClickHouse)
    [GeneratedRegex(@"^\s*OPTIMIZE\s+TABLE\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OptimizeTableRegex();

    // RENAME TABLE name TO ... (ClickHouse, MySQL) — captures the source table
    [GeneratedRegex(@"^\s*RENAME\s+(?:TABLE|DICTIONARY|DATABASE)\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RenameTableRegex();

    // ATTACH/DETACH TABLE|DATABASE|DICTIONARY|VIEW name (ClickHouse)
    [GeneratedRegex(@"^\s*(?:ATTACH|DETACH)\s+(?:TABLE|DATABASE|DICTIONARY|VIEW|PERMANENTLY\s+TABLE)\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AttachDetachRegex();

    // Matches INSERT OR (UPDATE|REPLACE|IGNORE) INTO pattern
    [GeneratedRegex(@"^\s*INSERT\s+OR\s+(?<modifier>UPDATE|REPLACE|IGNORE)\s+(?:INTO\s+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InsertOrModifierRegex();

    // Detects ON CONFLICT ... DO UPDATE (PostgreSQL, SQLite, Spanner)
    [GeneratedRegex(@"\bON\s+CONFLICT\b.*?\bDO\s+UPDATE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex OnConflictDoUpdateRegex();

    // Detects ON DUPLICATE KEY UPDATE (MySQL)
    [GeneratedRegex(@"\bON\s+DUPLICATE\s+KEY\s+UPDATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OnDuplicateKeyUpdateRegex();

    // Table name after FROM — supports [brackets], "quotes", `backticks`
    [GeneratedRegex(@"\bFROM\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FromTableRegex();

    // Table name after INTO
    [GeneratedRegex(@"\bINTO\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IntoTableRegex();

    // Table name after UPDATE keyword (at start)
    [GeneratedRegex(@"^\s*UPDATE\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UpdateTableRegex();

    // Table name after DELETE (optional FROM)
    [GeneratedRegex(@"^\s*DELETE\s+(?:FROM\s+)?" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DeleteTableRegex();

    // Table name after MERGE (optional INTO)
    [GeneratedRegex(@"^\s*MERGE\s+(?:INTO\s+)?" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MergeTableRegex();

    // Proc name after EXEC/EXECUTE
    [GeneratedRegex(@"^\s*(?:EXEC|EXECUTE)\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExecProcRegex();

    // Proc name after CALL
    [GeneratedRegex(@"^\s*CALL\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CallProcRegex();

    // DDL table name after CREATE TABLE / ALTER TABLE / DROP TABLE
    [GeneratedRegex(@"^\s*(?:CREATE|ALTER|DROP)\s+(?:TABLE|INDEX)\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DdlTableRegex();

    // TRUNCATE TABLE name
    [GeneratedRegex(@"^\s*TRUNCATE\s+TABLE\s+" + @"(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TruncateTableRegex();

    /// <summary>The only characters that can begin a comment or a string literal.</summary>
    private static readonly char[] StripTriggers = ['-', '/', '\''];

    /// <summary>
    /// Replaces every <c>--</c> comment, <c>/* */</c> comment and <c>'…'</c> string literal in
    /// <paramref name="sql"/> with a single space, so that the classifier's regular expressions only ever
    /// see code. Returns the statement unchanged when it contains none of them, which is the common case.
    /// <para>
    /// One pass, because the three forms nest into each other: a <c>--</c> inside a literal is not a
    /// comment, and a quote inside a comment does not open one. Doubled quotes (<c>''</c>) are the ANSI
    /// escape and do not end a literal. Bracketed, double-quoted and backticked text is left alone — those
    /// delimit <em>identifiers</em>, and the table extractors read through them.
    /// </para>
    /// <para>
    /// Malformed input is closed at the end of the statement rather than rejected: an unterminated comment
    /// or literal swallows the rest, which still leaves the leading keyword and any earlier clause
    /// readable.
    /// </para>
    /// <para>
    /// This is a classification aid only. What a report displays is <c>CommandText</c>, which stays
    /// exactly as it was captured.
    /// </para>
    /// </summary>
    private static string StripCommentsAndLiterals(string sql)
    {
        if (sql.IndexOfAny(StripTriggers) < 0)
            return sql;

        var sb = new StringBuilder(sql.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n') i++;
                sb.Append(' '); // the newline itself is left in place
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = Math.Min(sql.Length, i + 2);
                sb.Append(' ');
                continue;
            }

            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] != '\'') { i++; continue; }
                    if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                    i++;
                    break;
                }
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    public static UnifiedSqlOperationInfo Classify(string? commandText, CommandType commandType = CommandType.Text)
    {
        if (commandType == CommandType.StoredProcedure)
        {
            var procName = ExtractLastIdentifierPart(commandText);
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.StoredProcedure, procName, commandText);
        }

        if (string.IsNullOrWhiteSpace(commandText))
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.Other, null, commandText);

        // Comments and string literals first, because everything after this reads the statement with
        // regular expressions and has no way to tell code from prose. Two of the table extractors are
        // unanchored, so `-- rolled up from the weekly aggregate` in front of the real clause used to be
        // reported as the table `the`; and the keyword matcher and all three prefix strippers below are
        // `^`-anchored, so a statement that opened with a comment classified as `Other` with no table.
        var sql = StripCommentsAndLiterals(commandText);

        // Strip prefixes: Spanner hints, SET statements, CTEs
        sql = SpannerHintRegex().Replace(sql, "");
        sql = SetPrefixRegex().Replace(sql, "");
        sql = CtePrefixRegex().Replace(sql, "");

        var keywordMatch = FirstKeywordRegex().Match(sql);
        if (!keywordMatch.Success)
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.Other, null, commandText);

        var keyword = keywordMatch.Groups["keyword"].Value.ToUpperInvariant();

        return keyword switch
        {
            "SELECT" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Select, ExtractSelectTable(sql), commandText),
            "INSERT" => ClassifyInsert(sql, commandText),
            "UPDATE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Update, ExtractUpdateTable(sql), commandText),
            "DELETE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Delete, ExtractDeleteTable(sql), commandText),
            "MERGE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Merge, ExtractMergeTable(sql), commandText),
            "EXEC" or "EXECUTE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.StoredProcedure, ExtractExecProc(sql), commandText),
            "CALL" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.StoredProcedure, ExtractCallProc(sql), commandText),
            "CREATE" => ClassifyDdl(sql, commandText, UnifiedSqlOperation.CreateTable),
            "ALTER" => ClassifyAlter(sql, commandText),
            "DROP" => ClassifyDdl(sql, commandText, UnifiedSqlOperation.DropTable),
            "OPTIMIZE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Optimize, ExtractOptimizeTable(sql), commandText),
            "RENAME" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Rename, ExtractRenameTable(sql), commandText),
            "ATTACH" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Attach, ExtractAttachDetachTable(sql), commandText),
            "DETACH" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Detach, ExtractAttachDetachTable(sql), commandText),
            "TRUNCATE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Truncate, ExtractTruncateTable(sql), commandText),
            "BEGIN" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.BeginTransaction, null, commandText),
            "COMMIT" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Commit, null, commandText),
            "ROLLBACK" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Rollback, null, commandText),
            _ => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Other, null, commandText),
        };
    }

    /// <summary>
    /// Returns a diagram label for the given operation info and verbosity level.
    /// </summary>
    public static string GetDiagramLabel(UnifiedSqlOperationInfo op, SqlTrackingVerbosityLevel verbosity)
    {
        return verbosity switch
        {
            SqlTrackingVerbosityLevel.Raw => op.CommandText ?? op.Operation.ToString(),
            SqlTrackingVerbosityLevel.Detailed => op.Operation switch
            {
                UnifiedSqlOperation.Select => $"SELECT FROM {op.TableName ?? "?"}",
                UnifiedSqlOperation.Insert => $"INSERT INTO {op.TableName ?? "?"}",
                UnifiedSqlOperation.Update => $"UPDATE {op.TableName ?? "?"}",
                UnifiedSqlOperation.Delete => $"DELETE FROM {op.TableName ?? "?"}",
                UnifiedSqlOperation.Upsert => $"UPSERT {op.TableName ?? "?"}",
                UnifiedSqlOperation.Merge => $"MERGE {op.TableName ?? "?"}",
                UnifiedSqlOperation.StoredProcedure => $"EXEC {ExtractProcName(op.CommandText)}",
                UnifiedSqlOperation.CreateTable => $"CREATE TABLE {op.TableName ?? "?"}",
                UnifiedSqlOperation.AlterTable => $"ALTER TABLE {op.TableName ?? "?"}",
                UnifiedSqlOperation.DropTable => $"DROP TABLE {op.TableName ?? "?"}",
                UnifiedSqlOperation.CreateIndex => $"CREATE INDEX {op.TableName ?? "?"}",
                UnifiedSqlOperation.Truncate => $"TRUNCATE {op.TableName ?? "?"}",
                UnifiedSqlOperation.Optimize => $"OPTIMIZE {op.TableName ?? "?"}",
                UnifiedSqlOperation.Rename => $"RENAME {op.TableName ?? "?"}",
                UnifiedSqlOperation.Attach => $"ATTACH {op.TableName ?? "?"}",
                UnifiedSqlOperation.Detach => $"DETACH {op.TableName ?? "?"}",
                _ => op.Operation.ToString()
            },
            SqlTrackingVerbosityLevel.Summarised => op.Operation switch
            {
                UnifiedSqlOperation.Select => "SELECT",
                UnifiedSqlOperation.Insert => "INSERT",
                UnifiedSqlOperation.Update => "UPDATE",
                UnifiedSqlOperation.Delete => "DELETE",
                UnifiedSqlOperation.Upsert => "UPSERT",
                UnifiedSqlOperation.Merge => "MERGE",
                UnifiedSqlOperation.StoredProcedure => "EXEC",
                UnifiedSqlOperation.CreateTable => "CREATE TABLE",
                UnifiedSqlOperation.AlterTable => "ALTER TABLE",
                UnifiedSqlOperation.DropTable => "DROP TABLE",
                UnifiedSqlOperation.CreateIndex => "CREATE INDEX",
                UnifiedSqlOperation.Truncate => "TRUNCATE",
                UnifiedSqlOperation.Optimize => "OPTIMIZE",
                UnifiedSqlOperation.Rename => "RENAME",
                UnifiedSqlOperation.Attach => "ATTACH",
                UnifiedSqlOperation.Detach => "DETACH",
                UnifiedSqlOperation.BeginTransaction => "BEGIN",
                UnifiedSqlOperation.Commit => "COMMIT",
                UnifiedSqlOperation.Rollback => "ROLLBACK",
                _ => op.Operation.ToString()
            },
            _ => op.Operation.ToString()
        };
    }

    /// <summary>
    /// Gets the raw SQL keyword from command text (for Raw verbosity arrow labels).
    /// </summary>
    public static string? GetRawKeyword(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return null;

        var sql = commandText;
        sql = SpannerHintRegex().Replace(sql, "");
        sql = SetPrefixRegex().Replace(sql, "");
        sql = CtePrefixRegex().Replace(sql, "");

        var match = FirstKeywordRegex().Match(sql);
        return match.Success ? match.Groups["keyword"].Value.ToUpperInvariant() : null;
    }

    public static string ExtractProcName(string? commandText)
    {
        if (commandText is null) return "?";
        var trimmed = commandText.TrimStart();
        if (trimmed.StartsWith("EXEC ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[5..].TrimStart();
        else if (trimmed.StartsWith("EXECUTE ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[8..].TrimStart();
        else if (trimmed.StartsWith("CALL ", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[5..].TrimStart();

        var spaceIdx = trimmed.IndexOf(' ');
        var parenIdx = trimmed.IndexOf('(');
        var endIdx = (spaceIdx, parenIdx) switch
        {
            ( > 0, > 0) => Math.Min(spaceIdx, parenIdx),
            ( > 0, _) => spaceIdx,
            (_, > 0) => parenIdx,
            _ => -1
        };
        return endIdx > 0 ? trimmed[..endIdx] : trimmed;
    }

    private static UnifiedSqlOperationInfo ClassifyInsert(string sql, string? originalText)
    {
        // Check INSERT OR (UPDATE|REPLACE|IGNORE) pattern
        var orMatch = InsertOrModifierRegex().Match(sql);
        if (orMatch.Success)
        {
            var modifier = orMatch.Groups["modifier"].Value.ToUpperInvariant();
            var tableName = ExtractIntoTable(sql) ?? ExtractTableAfterInsertOr(sql);
            return modifier switch
            {
                "UPDATE" or "REPLACE" => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Upsert, tableName, originalText),
                _ => new UnifiedSqlOperationInfo(UnifiedSqlOperation.Insert, tableName, originalText), // IGNORE
            };
        }

        var table = ExtractIntoTable(sql);

        // Check ON CONFLICT DO UPDATE (PostgreSQL, SQLite, Spanner)
        if (OnConflictDoUpdateRegex().IsMatch(sql))
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.Upsert, table, originalText);

        // Check ON DUPLICATE KEY UPDATE (MySQL)
        if (OnDuplicateKeyUpdateRegex().IsMatch(sql))
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.Upsert, table, originalText);

        return new UnifiedSqlOperationInfo(UnifiedSqlOperation.Insert, table, originalText);
    }

    private static UnifiedSqlOperationInfo ClassifyAlter(string sql, string? originalText)
    {
        // ClickHouse lightweight mutations: ALTER TABLE t UPDATE/DELETE behave like UPDATE/DELETE DML
        var mutation = AlterMutationRegex().Match(sql);
        if (mutation.Success)
        {
            var table = StripQuotes(mutation.Groups["table"].Value);
            return mutation.Groups["mutation"].Value.ToUpperInvariant() == "DELETE"
                ? new UnifiedSqlOperationInfo(UnifiedSqlOperation.Delete, table, originalText)
                : new UnifiedSqlOperationInfo(UnifiedSqlOperation.Update, table, originalText);
        }

        return ClassifyDdl(sql, originalText, UnifiedSqlOperation.AlterTable);
    }

    private static UnifiedSqlOperationInfo ClassifyDdl(string sql, string? originalText, UnifiedSqlOperation defaultOp)
    {
        // Check if it's CREATE INDEX vs CREATE TABLE
        if (sql.TrimStart().StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) ||
            sql.TrimStart().StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))
        {
            var table = ExtractDdlTable(sql);
            return new UnifiedSqlOperationInfo(UnifiedSqlOperation.CreateIndex, table, originalText);
        }

        var ddlTable = ExtractDdlTable(sql);
        return new UnifiedSqlOperationInfo(defaultOp, ddlTable, originalText);
    }

    private static string? ExtractSelectTable(string sql)
    {
        var match = FromTableRegex().Match(sql);
        if (!match.Success) return null;

        var table = match.Groups["table"].Value;
        return table.StartsWith('(') ? null : StripQuotes(table);
    }

    private static string? ExtractIntoTable(string sql)
    {
        var match = IntoTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractTableAfterInsertOr(string sql)
    {
        var match = IntoTableRegex().Match(sql);
        if (match.Success) return StripQuotes(match.Groups["table"].Value);

        var afterModifier = InsertOrModifierRegex().Replace(sql, "");
        var identMatch = Regex.Match(afterModifier, @"^\s*(?:[\[\]""`]?[\w=+/]+[\[\]""`]?\.)*[\[\]""`]?(?<table>[\w=+/]+)[\[\]""`]?", RegexOptions.IgnoreCase);
        return identMatch.Success ? StripQuotes(identMatch.Groups["table"].Value) : null;
    }

    private static string? ExtractUpdateTable(string sql)
    {
        var match = UpdateTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractDeleteTable(string sql)
    {
        var match = DeleteTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractMergeTable(string sql)
    {
        var match = MergeTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractExecProc(string sql)
    {
        var match = ExecProcRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractCallProc(string sql)
    {
        var match = CallProcRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractDdlTable(string sql)
    {
        var match = DdlTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractTruncateTable(string sql)
    {
        var match = TruncateTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractOptimizeTable(string sql)
    {
        var match = OptimizeTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractRenameTable(string sql)
    {
        var match = RenameTableRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractAttachDetachTable(string sql)
    {
        var match = AttachDetachRegex().Match(sql);
        return match.Success ? StripQuotes(match.Groups["table"].Value) : null;
    }

    private static string? ExtractLastIdentifierPart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var clean = StripQuotes(text.Trim());
        var dotIndex = clean.LastIndexOf('.');
        return dotIndex >= 0 ? clean[(dotIndex + 1)..] : clean;
    }

    private static string StripQuotes(string value)
    {
        return value
            .Replace("[", "")
            .Replace("]", "")
            .Replace("\"", "")
            .Replace("`", "");
    }
}
