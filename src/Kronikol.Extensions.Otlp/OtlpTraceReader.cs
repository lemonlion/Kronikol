using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// Decodes an OTLP <c>ExportTraceServiceRequest</c> (equivalently a <c>TracesData</c>) into
/// <see cref="OtlpSpan"/>s, from either encoding the OTLP/HTTP spec defines:
/// <c>application/x-protobuf</c> and <c>application/json</c>.
/// </summary>
/// <remarks>
/// <para>The protobuf side is a hand-written reader over the wire format rather than generated
/// message classes: OTLP's trace schema is stable and this receiver reads exactly six message
/// types, so the package stays dependency-free (there is no first-party NuGet package of the OTLP
/// protobuf DTOs — the OpenTelemetry .NET exporter keeps its generated classes internal).</para>
/// <para>The JSON side follows the OTLP/JSON encoding (hex trace/span ids, int64 as strings,
/// <c>kind</c> as an integer or an enum name) and also accepts the base64 ids emitted by producers
/// that use the stock protobuf-to-JSON mapping.</para>
/// <para>Malformed input yields as many spans as could be read; it never throws
/// (<see cref="ReadJson"/> tolerates a truncated document, <see cref="ReadProtobuf"/> stops at the
/// first field it cannot walk).</para>
/// </remarks>
public static class OtlpTraceReader
{
    /// <summary>The OTLP/HTTP protobuf content type.</summary>
    public const string ProtobufContentType = "application/x-protobuf";

    /// <summary>The OTLP/HTTP JSON content type.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>
    /// Decodes a payload, choosing the encoding from <paramref name="contentType"/> (protobuf unless it
    /// mentions <c>json</c> — the OTLP/HTTP default).
    /// </summary>
    public static IReadOnlyList<OtlpSpan> Read(ReadOnlySpan<byte> payload, string? contentType)
    {
        var isJson = contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
        if (!isJson && contentType is null && LooksLikeJson(payload))
            isJson = true;
        return isJson ? ReadJson(payload) : ReadProtobuf(payload);
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> payload)
    {
        foreach (var b in payload)
        {
            if (b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
                continue;
            return b == (byte)'{';
        }

        return false;
    }

    // ------------------------------------------------------------------ JSON

    /// <summary>Decodes the OTLP/JSON encoding.</summary>
    public static IReadOnlyList<OtlpSpan> ReadJson(ReadOnlySpan<byte> payload)
    {
        var spans = new List<OtlpSpan>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload.ToArray());
        }
        catch (JsonException)
        {
            return spans;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("resourceSpans", out var resourceSpans)
                && !document.RootElement.TryGetProperty("resource_spans", out resourceSpans))
                return spans;
            if (resourceSpans.ValueKind != JsonValueKind.Array)
                return spans;

            foreach (var resourceSpan in resourceSpans.EnumerateArray())
            {
                var resourceAttributes = OtlpSpan.NoAttributes;
                if (TryProperty(resourceSpan, "resource", out var resource)
                    && TryProperty(resource, "attributes", out var resourceAttributeArray))
                    resourceAttributes = ReadJsonAttributes(resourceAttributeArray);

                if (!TryProperty(resourceSpan, "scopeSpans", "scope_spans", out var scopeSpans)
                    || scopeSpans.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var scopeSpan in scopeSpans.EnumerateArray())
                {
                    string? scopeName = null;
                    if (TryProperty(scopeSpan, "scope", out var scope) && TryProperty(scope, "name", out var scopeNameElement))
                        scopeName = scopeNameElement.GetString();

                    if (!TryProperty(scopeSpan, "spans", out var spanArray) || spanArray.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var span in spanArray.EnumerateArray())
                        spans.Add(ReadJsonSpan(span, resourceAttributes, scopeName));
                }
            }
        }

        return spans;
    }

    private static OtlpSpan ReadJsonSpan(JsonElement span, IReadOnlyDictionary<string, string> resourceAttributes, string? scopeName)
    {
        var attributes = TryProperty(span, "attributes", out var attributeArray)
            ? ReadJsonAttributes(attributeArray)
            : OtlpSpan.NoAttributes;

        var status = OtlpStatusCode.Unset;
        string? statusMessage = null;
        if (TryProperty(span, "status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Object)
        {
            if (TryProperty(statusElement, "code", out var code))
                status = ParseStatusCode(code);
            if (TryProperty(statusElement, "message", out var message))
                statusMessage = message.GetString();
        }

        return new OtlpSpan
        {
            TraceId = ReadJsonId(span, "traceId", "trace_id", 32),
            SpanId = ReadJsonId(span, "spanId", "span_id", 16),
            ParentSpanId = NullIfEmptyOrZero(ReadJsonId(span, "parentSpanId", "parent_span_id", 16)),
            Name = TryProperty(span, "name", out var name) ? name.GetString() ?? "" : "",
            Kind = ParseKind(span),
            StartTimeUnixNano = ReadJsonUInt64(span, "startTimeUnixNano", "start_time_unix_nano"),
            EndTimeUnixNano = ReadJsonUInt64(span, "endTimeUnixNano", "end_time_unix_nano"),
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            StatusCode = status,
            StatusMessage = NullIfEmpty(statusMessage),
            ScopeName = NullIfEmpty(scopeName),
        };
    }

    private static OtlpSpanKind ParseKind(JsonElement span)
    {
        if (!TryProperty(span, "kind", out var kind))
            return OtlpSpanKind.Unspecified;

        if (kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out var numeric))
            return ToKind(numeric);

        var text = kind.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return OtlpSpanKind.Unspecified;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return ToKind(parsed);

        // "SPAN_KIND_CLIENT" / "Client" / "client"
        var trimmed = text.Trim();
        if (trimmed.StartsWith("SPAN_KIND_", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["SPAN_KIND_".Length..];
        return Enum.TryParse<OtlpSpanKind>(trimmed, ignoreCase: true, out var named) ? named : OtlpSpanKind.Unspecified;
    }

    private static OtlpSpanKind ToKind(int value) => value is >= 0 and <= 5 ? (OtlpSpanKind)value : OtlpSpanKind.Unspecified;

    private static OtlpStatusCode ParseStatusCode(JsonElement code)
    {
        if (code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var numeric))
            return numeric is >= 0 and <= 2 ? (OtlpStatusCode)numeric : OtlpStatusCode.Unset;

        var text = code.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
            return OtlpStatusCode.Unset;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed is >= 0 and <= 2 ? (OtlpStatusCode)parsed : OtlpStatusCode.Unset;
        if (text.StartsWith("STATUS_CODE_", StringComparison.OrdinalIgnoreCase))
            text = text["STATUS_CODE_".Length..];
        return Enum.TryParse<OtlpStatusCode>(text, ignoreCase: true, out var named) ? named : OtlpStatusCode.Unset;
    }

    private static IReadOnlyDictionary<string, string> ReadJsonAttributes(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Array)
            return OtlpSpan.NoAttributes;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in attributes.EnumerateArray())
        {
            if (!TryProperty(attribute, "key", out var key))
                continue;
            var name = key.GetString();
            if (string.IsNullOrEmpty(name))
                continue;
            result[name] = TryProperty(attribute, "value", out var value) ? ReadJsonAnyValue(value) : "";
        }

        return result;
    }

    private static string ReadJsonAnyValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return value.ValueKind is JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

        if (TryProperty(value, "stringValue", "string_value", out var stringValue))
            return stringValue.ValueKind == JsonValueKind.String ? stringValue.GetString() ?? "" : stringValue.ToString();
        if (TryProperty(value, "intValue", "int_value", out var intValue))
            return intValue.ValueKind == JsonValueKind.String ? intValue.GetString() ?? "" : intValue.ToString();
        if (TryProperty(value, "doubleValue", "double_value", out var doubleValue))
            return doubleValue.ValueKind == JsonValueKind.Number && doubleValue.TryGetDouble(out var d)
                ? d.ToString("R", CultureInfo.InvariantCulture)
                : doubleValue.ToString();
        if (TryProperty(value, "boolValue", "bool_value", out var boolValue))
            return boolValue.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => boolValue.ToString(),
            };
        if (TryProperty(value, "bytesValue", "bytes_value", out var bytesValue))
            return bytesValue.GetString() ?? "";
        if (TryProperty(value, "arrayValue", "array_value", out var arrayValue))
        {
            if (!TryProperty(arrayValue, "values", out var values) || values.ValueKind != JsonValueKind.Array)
                return "[]";
            return "[" + string.Join(",", values.EnumerateArray().Select(ReadJsonAnyValue)) + "]";
        }

        if (TryProperty(value, "kvlistValue", "kvlist_value", out var kvlist))
        {
            if (!TryProperty(kvlist, "values", out var pairs) || pairs.ValueKind != JsonValueKind.Array)
                return "{}";
            var flattened = ReadJsonAttributes(pairs);
            return "{" + string.Join(",", flattened.Select(p => $"{p.Key}={p.Value}")) + "}";
        }

        return "";
    }

    private static string ReadJsonId(JsonElement span, string camel, string snake, int hexLength)
    {
        if (!TryProperty(span, camel, snake, out var element) || element.ValueKind != JsonValueKind.String)
            return "";
        return NormaliseId(element.GetString(), hexLength);
    }

    private static ulong ReadJsonUInt64(JsonElement span, string camel, string snake)
    {
        if (!TryProperty(span, camel, snake, out var element))
            return 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out var numeric))
            return numeric;
        var text = element.GetString();
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return element.TryGetProperty(name, out value);
        value = default;
        return false;
    }

    private static bool TryProperty(JsonElement element, string camel, string snake, out JsonElement value) =>
        TryProperty(element, camel, out value) || TryProperty(element, snake, out value);

    /// <summary>
    /// Normalises an id to lowercase hex of <paramref name="hexLength"/> characters. Accepts the OTLP/JSON
    /// hex form and the base64 form produced by the stock protobuf-to-JSON mapping.
    /// </summary>
    internal static string NormaliseId(string? value, int hexLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var trimmed = value.Trim();
        if (trimmed.Length == hexLength && trimmed.All(Uri.IsHexDigit))
            return trimmed.ToLowerInvariant();

        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            if (bytes.Length == hexLength / 2)
                return ToHex(bytes);
        }
        catch (FormatException)
        {
            // Not base64 either — fall through.
        }

        return trimmed.All(Uri.IsHexDigit) ? trimmed.ToLowerInvariant() : "";
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NullIfEmptyOrZero(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.All(c => c == '0') ? null : value;

    // ------------------------------------------------------------------ protobuf

    /// <summary>Decodes the OTLP protobuf encoding (<c>ExportTraceServiceRequest</c> / <c>TracesData</c>).</summary>
    public static IReadOnlyList<OtlpSpan> ReadProtobuf(ReadOnlySpan<byte> payload)
    {
        var spans = new List<OtlpSpan>();
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 1 && wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var resourceSpans))
                ReadResourceSpans(resourceSpans, spans);
            else if (!reader.TrySkip(wireType))
                break;
        }

        return spans;
    }

    private static void ReadResourceSpans(ReadOnlySpan<byte> payload, List<OtlpSpan> spans)
    {
        var resourceAttributes = OtlpSpan.NoAttributes;
        var scopeSpanPayloads = new List<byte[]>();

        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (wireType != ProtobufReader.LengthDelimited)
            {
                if (!reader.TrySkip(wireType)) break;
                continue;
            }

            if (!reader.TryReadBytes(out var value))
                break;

            switch (field)
            {
                case 1: // resource
                    resourceAttributes = ReadProtobufAttributes(value, field: 1);
                    break;
                case 2: // scope_spans
                    scopeSpanPayloads.Add(value.ToArray());
                    break;
            }
        }

        foreach (var scopeSpans in scopeSpanPayloads)
            ReadScopeSpans(scopeSpans, resourceAttributes, spans);
    }

    private static void ReadScopeSpans(ReadOnlySpan<byte> payload, IReadOnlyDictionary<string, string> resourceAttributes, List<OtlpSpan> spans)
    {
        string? scopeName = null;
        var spanPayloads = new List<byte[]>();

        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (wireType != ProtobufReader.LengthDelimited)
            {
                if (!reader.TrySkip(wireType)) break;
                continue;
            }

            if (!reader.TryReadBytes(out var value))
                break;

            switch (field)
            {
                case 1: // scope
                    scopeName = ReadScopeName(value);
                    break;
                case 2: // spans
                    spanPayloads.Add(value.ToArray());
                    break;
            }
        }

        foreach (var span in spanPayloads)
            spans.Add(ReadProtobufSpan(span, resourceAttributes, scopeName));
    }

    private static string? ReadScopeName(ReadOnlySpan<byte> payload)
    {
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 1 && wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var value))
                return Encoding.UTF8.GetString(value);
            if (!reader.TrySkip(wireType))
                break;
        }

        return null;
    }

    private static OtlpSpan ReadProtobufSpan(ReadOnlySpan<byte> payload, IReadOnlyDictionary<string, string> resourceAttributes, string? scopeName)
    {
        var traceId = "";
        var spanId = "";
        string? parentSpanId = null;
        var name = "";
        var kind = OtlpSpanKind.Unspecified;
        ulong start = 0, end = 0;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var status = OtlpStatusCode.Unset;
        string? statusMessage = null;

        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (wireType == ProtobufReader.LengthDelimited)
            {
                if (!reader.TryReadBytes(out var value))
                    break;
                switch (field)
                {
                    case 1: traceId = ToHex(value); break;
                    case 2: spanId = ToHex(value); break;
                    case 4: parentSpanId = value.Length == 0 ? null : ToHex(value); break;
                    case 5: name = Encoding.UTF8.GetString(value); break;
                    case 9: ReadKeyValueInto(value, attributes); break;
                    case 15: (status, statusMessage) = ReadStatus(value); break;
                }

                continue;
            }

            if (wireType == ProtobufReader.VarInt)
            {
                if (!reader.TryReadVarint(out var varint))
                    break;
                if (field == 6)
                    kind = varint <= 5 ? (OtlpSpanKind)varint : OtlpSpanKind.Unspecified;
                continue;
            }

            if (wireType == ProtobufReader.Fixed64)
            {
                if (!reader.TryReadFixed64(out var fixed64))
                    break;
                if (field == 7) start = fixed64;
                else if (field == 8) end = fixed64;
                continue;
            }

            if (!reader.TrySkip(wireType))
                break;
        }

        return new OtlpSpan
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = string.IsNullOrEmpty(parentSpanId) || parentSpanId.All(c => c == '0') ? null : parentSpanId,
            Name = name,
            Kind = kind,
            StartTimeUnixNano = start,
            EndTimeUnixNano = end,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            StatusCode = status,
            StatusMessage = string.IsNullOrWhiteSpace(statusMessage) ? null : statusMessage,
            ScopeName = string.IsNullOrWhiteSpace(scopeName) ? null : scopeName,
        };
    }

    private static (OtlpStatusCode Code, string? Message) ReadStatus(ReadOnlySpan<byte> payload)
    {
        var code = OtlpStatusCode.Unset;
        string? message = null;
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 2 && wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var value))
            {
                message = Encoding.UTF8.GetString(value);
                continue;
            }

            if (field == 3 && wireType == ProtobufReader.VarInt && reader.TryReadVarint(out var varint))
            {
                code = varint <= 2 ? (OtlpStatusCode)varint : OtlpStatusCode.Unset;
                continue;
            }

            if (!reader.TrySkip(wireType))
                break;
        }

        return (code, message);
    }

    /// <summary>Reads the repeated <c>KeyValue</c> field <paramref name="field"/> of a message (Resource.attributes = 1).</summary>
    private static IReadOnlyDictionary<string, string> ReadProtobufAttributes(ReadOnlySpan<byte> payload, int field)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var number, out var wireType))
        {
            if (number == field && wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var value))
            {
                ReadKeyValueInto(value, result);
                continue;
            }

            if (!reader.TrySkip(wireType))
                break;
        }

        return result;
    }

    private static void ReadKeyValueInto(ReadOnlySpan<byte> payload, Dictionary<string, string> into)
    {
        string? key = null;
        var value = "";
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var bytes))
            {
                if (field == 1) key = Encoding.UTF8.GetString(bytes);
                else if (field == 2) value = ReadAnyValue(bytes);
                continue;
            }

            if (!reader.TrySkip(wireType))
                break;
        }

        if (!string.IsNullOrEmpty(key))
            into[key] = value;
    }

    private static string ReadAnyValue(ReadOnlySpan<byte> payload)
    {
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (wireType)
            {
                case ProtobufReader.LengthDelimited when reader.TryReadBytes(out var bytes):
                    switch (field)
                    {
                        case 1: return Encoding.UTF8.GetString(bytes);
                        case 5: return ReadArrayValue(bytes);
                        case 6:
                        {
                            var pairs = ReadProtobufAttributes(bytes, field: 1);
                            return "{" + string.Join(",", pairs.Select(p => $"{p.Key}={p.Value}")) + "}";
                        }

                        case 7: return Convert.ToBase64String(bytes);
                        default: continue;
                    }

                case ProtobufReader.VarInt when reader.TryReadVarint(out var varint):
                    if (field == 2) return varint != 0 ? "true" : "false";
                    if (field == 3) return ((long)varint).ToString(CultureInfo.InvariantCulture);
                    continue;
                case ProtobufReader.Fixed64 when reader.TryReadFixed64(out var fixed64):
                    if (field == 4) return BitConverter.Int64BitsToDouble((long)fixed64).ToString("R", CultureInfo.InvariantCulture);
                    continue;
                default:
                    if (!reader.TrySkip(wireType))
                        return "";
                    continue;
            }
        }

        return "";
    }

    private static string ReadArrayValue(ReadOnlySpan<byte> payload)
    {
        var values = new List<string>();
        var reader = new ProtobufReader(payload);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 1 && wireType == ProtobufReader.LengthDelimited && reader.TryReadBytes(out var value))
            {
                values.Add(ReadAnyValue(value));
                continue;
            }

            if (!reader.TrySkip(wireType))
                break;
        }

        return "[" + string.Join(",", values) + "]";
    }

    internal static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return "";
        var chars = new char[bytes.Length * 2];
        const string hex = "0123456789abcdef";
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = hex[bytes[i] >> 4];
            chars[i * 2 + 1] = hex[bytes[i] & 0x0f];
        }

        return new string(chars);
    }

    /// <summary>A forward-only reader over the protobuf wire format — just enough for OTLP traces.</summary>
    private ref struct ProtobufReader(ReadOnlySpan<byte> buffer)
    {
        public const int VarInt = 0;
        public const int Fixed64 = 1;
        public const int LengthDelimited = 2;
        public const int Fixed32 = 5;

        private readonly ReadOnlySpan<byte> _buffer = buffer;
        private int _position;

        public bool TryReadTag(out int fieldNumber, out int wireType)
        {
            fieldNumber = 0;
            wireType = 0;
            if (!TryReadVarint(out var tag) || tag == 0)
                return false;
            fieldNumber = (int)(tag >> 3);
            wireType = (int)(tag & 0x7);
            return fieldNumber > 0;
        }

        public bool TryReadVarint(out ulong value)
        {
            value = 0;
            var shift = 0;
            while (_position < _buffer.Length && shift <= 63)
            {
                var b = _buffer[_position++];
                value |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                    return true;
                shift += 7;
            }

            return false;
        }

        public bool TryReadFixed64(out ulong value)
        {
            value = 0;
            if (_position + 8 > _buffer.Length)
                return false;
            value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer[_position..]);
            _position += 8;
            return true;
        }

        public bool TryReadBytes(out ReadOnlySpan<byte> value)
        {
            value = default;
            if (!TryReadVarint(out var length) || length > int.MaxValue)
                return false;
            var size = (int)length;
            if (_position + size > _buffer.Length)
                return false;
            value = _buffer.Slice(_position, size);
            _position += size;
            return true;
        }

        public bool TrySkip(int wireType)
        {
            switch (wireType)
            {
                case VarInt:
                    return TryReadVarint(out _);
                case Fixed64:
                    return TryReadFixed64(out _);
                case LengthDelimited:
                    return TryReadBytes(out _);
                case Fixed32:
                    if (_position + 4 > _buffer.Length)
                        return false;
                    _position += 4;
                    return true;
                default:
                    return false;
            }
        }
    }
}
