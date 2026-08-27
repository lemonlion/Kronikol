using System.Globalization;
using System.Text;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// Encodes <see cref="OtlpExportSpan"/>s as an OTLP/JSON <c>ExportTraceServiceRequest</c> — the encoding
/// twin of <see cref="OtlpTraceReader.ReadJson"/>, which is its round-trip oracle in the tests. Pure and
/// byte-stable: the same spans always encode to the same string, so golden tests can lock the format in.
/// </summary>
/// <remarks>
/// The JSON is written by hand (StringBuilder + standard JSON escaping), keeping the package
/// dependency-free. The OTLP/JSON conventions: trace/span ids as lowercase hex, <c>…UnixNano</c> and
/// <c>intValue</c> int64s as decimal strings, <c>kind</c>/<c>status.code</c> as their enum integers.
/// Spans are grouped into one <c>resourceSpans</c> entry per <see cref="OtlpExportSpan.ResourceServiceName"/>
/// (first-appearance order), each with a single scope.
/// </remarks>
public static class OtlpJsonEncoder
{
    /// <summary>The instrumentation scope name every exported span carries.</summary>
    public const string ScopeName = "Kronikol";

    /// <summary>The scope version — this assembly's version.</summary>
    public static readonly string ScopeVersion =
        typeof(OtlpJsonEncoder).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Encodes the spans as one <c>ExportTraceServiceRequest</c> JSON document.</summary>
    public static string Encode(IReadOnlyList<OtlpExportSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        // One resourceSpans entry per caller, in first-appearance order.
        var groups = new List<(string ServiceName, List<OtlpExportSpan> Spans)>();
        var byService = new Dictionary<string, List<OtlpExportSpan>>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            if (!byService.TryGetValue(span.ResourceServiceName, out var group))
            {
                group = [];
                byService[span.ResourceServiceName] = group;
                groups.Add((span.ResourceServiceName, group));
            }

            group.Add(span);
        }

        var json = new StringBuilder(256 + spans.Count * 256);
        json.Append("{\"resourceSpans\":[");
        for (var g = 0; g < groups.Count; g++)
        {
            if (g > 0)
                json.Append(',');
            var (serviceName, groupSpans) = groups[g];
            json.Append("{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"");
            AppendEscaped(json, serviceName);
            json.Append("\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"").Append(ScopeName)
                .Append("\",\"version\":\"").Append(ScopeVersion).Append("\"},\"spans\":[");
            for (var s = 0; s < groupSpans.Count; s++)
            {
                if (s > 0)
                    json.Append(',');
                AppendSpan(json, groupSpans[s]);
            }

            json.Append("]}]}");
        }

        json.Append("]}");
        return json.ToString();
    }

    private static void AppendSpan(StringBuilder json, OtlpExportSpan span)
    {
        json.Append("{\"traceId\":\"").Append(span.TraceId)
            .Append("\",\"spanId\":\"").Append(span.SpanId)
            .Append("\",\"name\":\"");
        AppendEscaped(json, span.Name);
        json.Append("\",\"kind\":").Append(((int)span.Kind).ToString(CultureInfo.InvariantCulture))
            .Append(",\"startTimeUnixNano\":\"").Append(span.StartTimeUnixNano.ToString(CultureInfo.InvariantCulture))
            .Append("\",\"endTimeUnixNano\":\"").Append(span.EndTimeUnixNano.ToString(CultureInfo.InvariantCulture))
            .Append("\",\"attributes\":[");

        for (var i = 0; i < span.Attributes.Count; i++)
        {
            if (i > 0)
                json.Append(',');
            AppendAttribute(json, span.Attributes[i]);
        }

        json.Append(']');

        if (span.Status != OtlpStatusCode.Unset)
        {
            json.Append(",\"status\":{\"code\":").Append(((int)span.Status).ToString(CultureInfo.InvariantCulture));
            if (span.StatusMessage is not null)
            {
                json.Append(",\"message\":\"");
                AppendEscaped(json, span.StatusMessage);
                json.Append('"');
            }

            json.Append('}');
        }

        json.Append('}');
    }

    private static void AppendAttribute(StringBuilder json, OtlpExportAttribute attribute)
    {
        json.Append("{\"key\":\"");
        AppendEscaped(json, attribute.Key);
        json.Append("\",\"value\":{");
        switch (attribute.Kind)
        {
            case OtlpAttributeValueKind.Int:
                json.Append("\"intValue\":\"").Append(attribute.Value).Append('"');
                break;
            case OtlpAttributeValueKind.Bool:
                json.Append("\"boolValue\":").Append(attribute.Value);
                break;
            default:
                json.Append("\"stringValue\":\"");
                AppendEscaped(json, attribute.Value);
                json.Append('"');
                break;
        }

        json.Append("}}");
    }

    /// <summary>Standard JSON string escaping: quote, backslash, and control characters as <c>\uXXXX</c> (with the short forms for the common ones).</summary>
    internal static void AppendEscaped(StringBuilder json, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                default:
                    if (c < 0x20)
                        json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        json.Append(c);
                    break;
            }
        }
    }
}
