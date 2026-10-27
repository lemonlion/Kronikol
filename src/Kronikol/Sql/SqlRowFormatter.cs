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

    /// <summary>
    /// A cell as a note shows it: bytes by their length, a long value cut with its full length after it, and a value
    /// System.Text.Json writes as it is (a number, text, a date or time, a GUID, an enum, a JSON document, or a list or
    /// map of those) kept as it is. Any other value is a provider's own type (a geography, a range, a network address)
    /// and is kept as its text: serialized as an object, its properties were read by reflection when the reader closed,
    /// and one that threw failed the application's own call (5.11.1).
    /// </summary>
    internal static object FormatCellValue(object value, int maxValueLen)
    {
        if (value is byte[] bytes)
            return $"[bytes: {bytes.Length}]";

        var str = TextOf(value);
        if (str.Length > Math.Max(0, maxValueLen))
            return Cut(str, maxValueLen);

        return IsPlainJsonValue(value) ? value : str;
    }

    /// <summary>
    /// A scalar result as a note shows it: <c>null</c>, or its <see cref="ValueText"/> cut at
    /// <paramref name="maxValueLen"/> with its full length after it.
    /// </summary>
    internal static string ScalarText(object? result, int maxValueLen) =>
        result is null or DBNull ? "null" : Cut(ValueText(result), maxValueLen);

    // MaxValueDisplayLength is a plain int: a negative one cuts at nothing, where the range it made threw into the
    // application's own call until 5.11.1.
    private static string Cut(string text, int maxValueLen)
    {
        var length = Math.Max(0, maxValueLen);
        return text.Length > length ? $"{text[..length]}... ({text.Length} chars)" : text;
    }

    /// <summary>
    /// A single value as a note shows it, a scalar result or a parameter, written as the same value is in a row (as JSON):
    /// numbers, dates and times the same on every machine, text as it is, bytes by their length. Until 4.16.3 each
    /// wrapper wrote it with the machine's culture, so <c>SELECT 2.5</c> answered <c>2,5</c> on a German machine. A
    /// value whose text cannot be had is shown as its type and the exception, never thrown.
    /// </summary>
    internal static string ValueText(object value)
    {
        try
        {
            return value switch
            {
                string text => text,
                byte[] bytes => $"[bytes: {bytes.Length}]",
                DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan => JsonSerializer.Serialize(value, value.GetType(), JsonOptions).Trim('"'),
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
        }
        catch (Exception ex)
        {
            return Unreadable(value, ex);
        }
    }

    private static string TextOf(object value)
    {
        try
        {
            return value.ToString() ?? "";
        }
        catch (Exception ex)
        {
            return Unreadable(value, ex);
        }
    }

    private static string Unreadable(object value, Exception ex) => $"({value.GetType().Name}: {ex.GetType().Name})";

    private static bool IsPlainJsonValue(object value) => value switch
    {
        double number => double.IsFinite(number),
        float number => float.IsFinite(number),
        _ => IsPlainJsonType(value.GetType()),
    };

    private static bool IsPlainJsonType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            return false;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
            || type == typeof(TimeSpan) || type == typeof(JsonElement) || typeof(JsonDocument).IsAssignableFrom(type) || type == typeof(byte[]))
            return true;
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsPlainJsonType(type.GetElementType()!);

        var map = GenericInterface(type, typeof(IDictionary<,>)) ?? GenericInterface(type, typeof(IReadOnlyDictionary<,>));
        if (map is not null)
            return map.GetGenericArguments()[0] == typeof(string) && IsPlainJsonType(map.GetGenericArguments()[1]);

        var sequence = GenericInterface(type, typeof(IEnumerable<>));
        return sequence is not null && IsPlainJsonType(sequence.GetGenericArguments()[0]);
    }

    private static Type? GenericInterface(Type type, Type definition) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == definition
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);

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

        sb.Append(capturedRows.Count > 0 ? RowsAsJson(capturedRows) ?? rowLabel : rowLabel);

        if (maxRows > 0 && totalRows > maxRows)
            sb.Append($"\n... ({totalRows - maxRows} more rows not shown)");

        return sb.ToString();
    }

    // Cells are plain values by FormatCellValue; a value inside a list that System.Text.Json still refuses (a NaN in a
    // float[]) leaves the note with the count rather than throwing.
    private static string? RowsAsJson(IReadOnlyList<object> rows)
    {
        try
        {
            return JsonSerializer.Serialize(rows, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
