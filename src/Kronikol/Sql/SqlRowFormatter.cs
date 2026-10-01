using System.Text;
using System.Text.Json;

namespace Kronikol.Sql;

/// <summary>
/// How captured rows read in a note: one rule for the rows a reader returned (<see cref="TrackingDbDataReader"/>) and
/// the rows a bulk insert sent (ClickHouse.Driver's <c>InsertBinaryAsync</c>), governed by the same
/// <see cref="SqlResponseDetail"/>, <c>MaxResponseRows</c> and <c>MaxValueDisplayLength</c>.
/// </summary>
internal static class SqlRowFormatter
{
    internal const int MaxDisplayedColumns = 20;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>A cell as a note shows it: bytes by their length, a long value cut with its full length after it.</summary>
    internal static object FormatCellValue(object value, int maxValueLen)
    {
        if (value is byte[] bytes)
            return $"[bytes: {bytes.Length}]";

        var str = value.ToString() ?? "";
        if (str.Length > maxValueLen)
            return $"{str[..maxValueLen]}... ({str.Length} chars)";

        return value;
    }

    /// <summary>The column list a count-and-columns note shows, cut after <see cref="MaxDisplayedColumns"/> names.</summary>
    internal static string? FormatColumnNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return null;

        if (names.Count <= MaxDisplayedColumns)
            return string.Join(", ", names);

        return $"{string.Join(", ", names.Take(MaxDisplayedColumns))} ... (+{names.Count - MaxDisplayedColumns} more)";
    }

    /// <summary>
    /// The note for <paramref name="totalRows"/> rows, of which <paramref name="capturedRows"/> are the first ones kept
    /// (each already formatted cell by cell): a count, a count and the columns, or the rows as JSON with a footer for
    /// those past <paramref name="maxRows"/>.
    /// </summary>
    internal static string Format(SqlResponseDetail detail, int maxRows, long totalRows, IReadOnlyList<object> capturedRows, string? columnNames)
    {
        var rowLabel = totalRows == 1 ? "1 row" : $"{totalRows} rows";

        if (detail == SqlResponseDetail.RowCountOnly)
            return rowLabel;

        if (detail == SqlResponseDetail.RowCountAndColumns || maxRows == 0)
            return columnNames is not null ? $"{rowLabel} [{columnNames}]" : rowLabel;

        // FullRows
        var sb = new StringBuilder();

        if (capturedRows.Count > 0)
            sb.Append(JsonSerializer.Serialize(capturedRows, JsonOptions));
        else
            sb.Append(rowLabel);

        if (maxRows > 0 && totalRows > maxRows)
            sb.Append($"\n... ({totalRows - maxRows} more rows not shown)");

        return sb.ToString();
    }
}
