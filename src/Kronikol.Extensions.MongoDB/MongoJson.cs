using System.Globalization;
using System.Text;
using global::MongoDB.Bson;

// Shared source: also compiled into Kronikol.Extensions.TcpTap (and Kronikol.Extensions.MongoDB.V2) as a
// linked file, so the wire tap writes a note byte for byte as the in-process subscriber does, whichever
// MongoDB.Bson the host resolves. The tap compiles it into its own namespace so a host can reference both packages.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// Writes a BSON value as MongoDB Extended JSON v2 in its relaxed form, on one line, spaced as the driver spaces it
/// (<c>{ "status" : "Ready", "n" : 1 }</c>).
/// </summary>
/// <remarks>
/// <para>Kronikol's own writer, not the driver's <c>ToJson</c>: <c>JsonOutputMode</c>'s values differ between
/// MongoDB.Bson 2.x and 3.x, so code compiled against one and run on the other writes the wrong dialect, and a host can
/// change <c>JsonWriterSettings.Defaults</c>. This reads only <see cref="BsonValue"/> members both majors have, so it
/// writes the same text in every package and on every host.</para>
/// <para>The spacing is the driver's because users' capture-time redaction patterns match a note's text, and they were
/// written against the driver's. Strings are escaped as MongoDB.Bson 3.x escapes them. Where it departs from the
/// driver: a double is the shortest text that reads back as the same double, with <c>.0</c> added when it has no
/// fraction or exponent (the driver writes 17 significant digits); a 16-byte binary of subtype 4 is
/// <c>{ "$uuid" : "…" }</c>, so a GUID can be found by its text; and, where the caller asks, a binary over
/// <see cref="MaxInlineBinaryBytes"/> bytes is a placeholder.</para>
/// <para>It never throws for a value it does not know: it writes <c>"[unreadable BSON: …]"</c>. Reading a document the
/// driver has disposed still throws, as any read of it does.</para>
/// </remarks>
internal static class MongoJson
{
    /// <summary>The largest binary written whole where the caller asks for placeholders. A GUID is 16 bytes, a GridFS chunk 255 KB.</summary>
    internal const int MaxInlineBinaryBytes = 256;

    /// <summary>Writes <paramref name="value"/> on one line.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="binaryPlaceholders">Whether a binary over <see cref="MaxInlineBinaryBytes"/> bytes is written as a placeholder.</param>
    internal static string Write(BsonValue value, bool binaryPlaceholders = false)
    {
        var builder = new StringBuilder();
        Write(builder, value, binaryPlaceholders);
        return builder.ToString();
    }

    /// <summary>Appends <paramref name="value"/> to <paramref name="builder"/>, on one line.</summary>
    internal static void Write(StringBuilder builder, BsonValue value, bool binaryPlaceholders)
    {
        switch (value.BsonType)
        {
            case BsonType.Document:
                WriteDocument(builder, value.AsBsonDocument, binaryPlaceholders);
                break;
            case BsonType.Array:
                WriteArray(builder, value.AsBsonArray, binaryPlaceholders);
                break;
            case BsonType.String:
                WriteString(builder, value.AsString);
                break;
            case BsonType.Int32:
                builder.Append(value.AsInt32.ToString(CultureInfo.InvariantCulture));
                break;
            case BsonType.Int64:
                builder.Append(value.AsInt64.ToString(CultureInfo.InvariantCulture));
                break;
            case BsonType.Double:
                WriteDouble(builder, value.AsDouble);
                break;
            case BsonType.Boolean:
                builder.Append(value.AsBoolean ? "true" : "false");
                break;
            case BsonType.Null:
                builder.Append("null");
                break;
            case BsonType.Decimal128:
                Wrapped(builder, "$numberDecimal", value.AsDecimal128.ToString());
                break;
            case BsonType.ObjectId:
                Wrapped(builder, "$oid", value.AsObjectId.ToString());
                break;
            case BsonType.DateTime:
                WriteDate(builder, value.AsBsonDateTime.MillisecondsSinceEpoch);
                break;
            case BsonType.Binary:
                WriteBinary(builder, value.AsBsonBinaryData, binaryPlaceholders);
                break;
            case BsonType.RegularExpression:
                var regex = value.AsBsonRegularExpression;
                builder.Append("{ \"$regularExpression\" : { \"pattern\" : ");
                WriteString(builder, regex.Pattern);
                builder.Append(", \"options\" : ");
                // The spec orders the options alphabetically. Bson stores them so, on 2.x and 3.x alike: its
                // constructor sorts them, and every read (the wire, a JSON parse) goes through it.
                WriteString(builder, regex.Options);
                builder.Append(" } }");
                break;
            case BsonType.Timestamp:
                var timestamp = value.AsBsonTimestamp;
                builder.Append("{ \"$timestamp\" : { \"t\" : ")
                    .Append(unchecked((uint)timestamp.Timestamp).ToString(CultureInfo.InvariantCulture))
                    .Append(", \"i\" : ")
                    .Append(unchecked((uint)timestamp.Increment).ToString(CultureInfo.InvariantCulture))
                    .Append(" } }");
                break;
            case BsonType.JavaScript:
                Wrapped(builder, "$code", value.AsBsonJavaScript.Code);
                break;
            case BsonType.JavaScriptWithScope:
                var code = value.AsBsonJavaScriptWithScope;
                builder.Append("{ \"$code\" : ");
                WriteString(builder, code.Code);
                builder.Append(", \"$scope\" : ");
                WriteDocument(builder, code.Scope, binaryPlaceholders);
                builder.Append(" }");
                break;
            case BsonType.Symbol:
                Wrapped(builder, "$symbol", value.AsBsonSymbol.Name);
                break;
            case BsonType.MinKey:
                builder.Append("{ \"$minKey\" : 1 }");
                break;
            case BsonType.MaxKey:
                builder.Append("{ \"$maxKey\" : 1 }");
                break;
            case BsonType.Undefined:
                builder.Append("{ \"$undefined\" : true }");
                break;
            default:
                WriteString(builder, $"[unreadable BSON: {value.BsonType}]");
                break;
        }
    }

    private static void WriteDocument(StringBuilder builder, BsonDocument document, bool binaryPlaceholders)
    {
        builder.Append('{');
        var first = true;
        foreach (var element in document)
        {
            builder.Append(first ? " " : ", ");
            first = false;
            WriteString(builder, element.Name);
            builder.Append(" : ");
            Write(builder, element.Value, binaryPlaceholders);
        }
        builder.Append(" }");
    }

    private static void WriteArray(StringBuilder builder, BsonArray array, bool binaryPlaceholders)
    {
        builder.Append('[');
        var first = true;
        foreach (var item in array)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            Write(builder, item, binaryPlaceholders);
        }
        builder.Append(']');
    }

    private static void Wrapped(StringBuilder builder, string name, string text)
    {
        builder.Append("{ \"").Append(name).Append("\" : ");
        WriteString(builder, text);
        builder.Append(" }");
    }

    private static void WriteDouble(StringBuilder builder, double value)
    {
        if (double.IsNaN(value))
        {
            Wrapped(builder, "$numberDouble", "NaN");
            return;
        }
        if (double.IsInfinity(value))
        {
            Wrapped(builder, "$numberDouble", value > 0 ? "Infinity" : "-Infinity");
            return;
        }

        // The shortest text that reads back as the same double; the relaxed form needs a fraction or an exponent,
        // or a reader takes 1.0 for the integer 1.
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOfAny(['.', 'E', 'e']) < 0)
            text += ".0";
        builder.Append(text);
    }

    /// <summary>The ISO form for 1970 to 9999, as the spec's relaxed form writes it; any other instant as milliseconds.</summary>
    private static void WriteDate(StringBuilder builder, long millisecondsSinceEpoch)
    {
        const long lastMillisecondOf9999 = 253402300799999;
        if (millisecondsSinceEpoch is < 0 or > lastMillisecondOf9999)
        {
            builder.Append("{ \"$date\" : { \"$numberLong\" : \"")
                .Append(millisecondsSinceEpoch.ToString(CultureInfo.InvariantCulture))
                .Append("\" } }");
            return;
        }

        var instant = DateTime.UnixEpoch.AddMilliseconds(millisecondsSinceEpoch);
        var format = instant.Millisecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss'Z'" : "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        builder.Append("{ \"$date\" : \"").Append(instant.ToString(format, CultureInfo.InvariantCulture)).Append("\" }");
    }

    private static void WriteBinary(StringBuilder builder, BsonBinaryData binary, bool binaryPlaceholders)
    {
        var bytes = binary.Bytes;
        var subType = (byte)binary.SubType;
        if (subType == 4 && bytes.Length == 16)
        {
            // The bytes of a subtype-4 GUID are in the RFC's order, so the text is read straight off them.
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            Wrapped(builder, "$uuid", $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}");
            return;
        }

        var subTypeText = subType.ToString("x2", CultureInfo.InvariantCulture);
        if (binaryPlaceholders && bytes.Length > MaxInlineBinaryBytes)
        {
            WriteString(builder, string.Create(CultureInfo.InvariantCulture, $"[binary: {bytes.Length:N0} bytes, subtype {subTypeText}]"));
            return;
        }

        builder.Append("{ \"$binary\" : { \"base64\" : \"")
            .Append(Convert.ToBase64String(bytes))
            .Append("\", \"subType\" : \"")
            .Append(subTypeText)
            .Append("\" } }");
    }

    /// <summary>A quoted string, escaped as MongoDB.Bson 3.x escapes it: what a reader, a search or a redaction pattern looks for is written as itself.</summary>
    internal static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (WrittenAsItself(c))
                        builder.Append(c);
                    else
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
            }
        }
        builder.Append('"');
    }

    /// <summary>
    /// The categories MongoDB.Bson 3.x writes as themselves: letters but modifier letters, numbers, space separators,
    /// punctuation and symbols. Marks, controls, format characters, separators other than spaces, surrogates,
    /// private-use and unassigned characters are written as <c>\u</c> escapes (measured over every UTF-16 unit).
    /// </summary>
    private static bool WrittenAsItself(char c) => CharUnicodeInfo.GetUnicodeCategory(c) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol => true,
        _ => false
    };
}
