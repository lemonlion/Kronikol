using System.Globalization;
using System.Text;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Kronikol.Extensions.Spanner;

public static class SpannerResponseFormatter
{
    private const int MaxDisplayedColumns = 20;
    private const int MaxCellDisplayLength = 500;

    /// <summary>
    /// Formats a ResultSet response (from ExecuteSql or Read).
    /// </summary>
    public static string? FormatResultSet(
        ResultSet response, SpannerResponseDetail detail, int maxRows)
    {
        maxRows = Math.Max(0, maxRows);
        var rowCount = response.Rows.Count;
        var columns = response.Metadata?.RowType?.Fields;

        var rowLabel = rowCount == 1 ? "1 row" : $"{rowCount} rows";

        if (detail == SpannerResponseDetail.RowCountOnly)
            return rowLabel;

        var columnNames = FormatColumnNames(columns);

        if (detail == SpannerResponseDetail.RowCountAndColumns || maxRows == 0)
            return columnNames is not null ? $"{rowLabel} [{columnNames}]" : rowLabel;

        // FullRows
        var sb = new StringBuilder();

        var displayCount = maxRows > 0 ? Math.Min(rowCount, maxRows) : rowCount;
        if (displayCount > 0)
        {
            for (var i = 0; i < displayCount; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(FormatRow(response.Rows[i], columns));
            }
        }
        else
        {
            sb.Append(rowLabel);
            if (columnNames is not null)
                sb.Append($" [{columnNames}]");
        }

        if (maxRows > 0 && rowCount > maxRows)
            sb.Append($"\n... ({rowCount - maxRows} more rows not shown)");

        return sb.ToString();
    }

    /// <summary>
    /// Formats a CommitResponse (commit timestamp).
    /// </summary>
    public static string? FormatCommitResponse(CommitResponse response)
    {
        if (response.CommitTimestamp is null || response.CommitTimestamp.Equals(new Timestamp()))
            return "Committed";

        return $"Committed at {response.CommitTimestamp.ToDateTimeOffset():O}";
    }

    /// <summary>
    /// Formats an ExecuteBatchDmlResponse (per-statement stats).
    /// </summary>
    public static string? FormatBatchDmlResponse(
        ExecuteBatchDmlResponse response, SpannerResponseDetail detail)
    {
        var count = response.ResultSets.Count;
        var sb = new StringBuilder();
        sb.Append($"{count} statement{(count == 1 ? "" : "s")}");

        if (detail != SpannerResponseDetail.RowCountOnly && count > 0)
        {
            var rowCounts = response.ResultSets
                .Select(rs => rs.Stats?.RowCountExact ?? 0)
                .Select(c => c == 1 ? "1 row" : $"{c} rows");
            sb.Append($" [{string.Join(", ", rowCounts)}]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats accumulated PartialResultSet chunks from a streaming response.
    /// </summary>
    public static string? FormatPartialResultSets(
        IReadOnlyList<PartialResultSet> chunks, SpannerResponseDetail detail, int maxRows)
    {
        if (chunks.Count == 0) return null;

        maxRows = Math.Max(0, maxRows);

        // Get metadata from first chunk
        var metadata = chunks[0].Metadata;
        var columns = metadata?.RowType?.Fields;
        var columnCount = columns?.Count ?? 1;

        // Count total values across all chunks
        var totalValues = 0;
        foreach (var chunk in chunks)
            totalValues += chunk.Values.Count;

        var rowCount = columnCount > 0 ? totalValues / columnCount : 0;
        var rowLabel = rowCount == 1 ? "1 row" : $"{rowCount} rows";

        if (detail == SpannerResponseDetail.RowCountOnly)
            return rowLabel;

        var columnNames = FormatColumnNames(columns);

        if (detail == SpannerResponseDetail.RowCountAndColumns || maxRows == 0)
            return columnNames is not null ? $"{rowLabel} [{columnNames}]" : rowLabel;

        // FullRows - reconstruct rows from flat values across chunks
        var allValues = new List<Value>();
        foreach (var chunk in chunks)
            allValues.AddRange(chunk.Values);

        var sb = new StringBuilder();
        sb.Append(rowLabel);
        if (columnNames is not null)
            sb.Append($" [{columnNames}]");

        var displayCount = maxRows > 0 ? Math.Min(rowCount, maxRows) : rowCount;
        for (var i = 0; i < displayCount; i++)
        {
            sb.AppendLine();
            var rowValues = new ListValue();
            for (var j = 0; j < columnCount && (i * columnCount + j) < allValues.Count; j++)
                rowValues.Values.Add(allValues[i * columnCount + j]);
            sb.Append(FormatRow(rowValues, columns));
        }

        if (maxRows > 0 && rowCount > maxRows)
            sb.Append($"\n... ({rowCount - maxRows} more)");

        return sb.ToString();
    }

    /// <summary>
    /// Gets the full JSON representation for Raw verbosity.
    /// </summary>
    public static string? FormatRaw<TResponse>(TResponse response)
    {
        if (response is IMessage protoMsg)
            return JsonFormatter.Default.Format(protoMsg);
        return null;
    }

    // ─── Private helpers ────────────────────────────────────

    private static string? FormatColumnNames(
        Google.Protobuf.Collections.RepeatedField<StructType.Types.Field>? fields)
    {
        if (fields is null || fields.Count == 0)
            return null;

        if (fields.Count <= MaxDisplayedColumns)
            return string.Join(", ", fields.Select(f => f.Name));

        var displayed = fields.Take(MaxDisplayedColumns).Select(f => f.Name);
        return $"{string.Join(", ", displayed)} ... (+{fields.Count - MaxDisplayedColumns} more)";
    }

    private static string FormatRow(
        ListValue row,
        Google.Protobuf.Collections.RepeatedField<StructType.Types.Field>? fields)
    {
        var sb = new StringBuilder("{");
        for (var i = 0; i < row.Values.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var fieldName = fields is not null && i < fields.Count ? fields[i].Name : $"col{i}";
            sb.Append($"{fieldName}: {FormatValue(row.Values[i])}");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string FormatValue(Value value)
    {
        if (value.KindCase == Value.KindOneofCase.NullValue)
            return "null";

        if (value.KindCase == Value.KindOneofCase.BoolValue)
            return value.BoolValue ? "true" : "false";

        if (value.KindCase == Value.KindOneofCase.NumberValue)
            // A point on every machine, as the row's other values are written: "G" alone wrote 99,5 under a
            // comma-decimal culture until 4.7.3, into the captured response itself.
            return value.NumberValue.ToString("G", CultureInfo.InvariantCulture);

        if (value.KindCase == Value.KindOneofCase.StringValue)
            return TruncateValue(value.StringValue);

        if (value.KindCase == Value.KindOneofCase.ListValue)
            return $"[{value.ListValue.Values.Count} items]";

        if (value.KindCase == Value.KindOneofCase.StructValue)
            return "{...}";

        return value.ToString() ?? "?";
    }

    private static string TruncateValue(string value)
    {
        if (value.Length <= MaxCellDisplayLength)
            return value;

        return $"{value[..MaxCellDisplayLength]}... ({value.Length} chars)";
    }
}
