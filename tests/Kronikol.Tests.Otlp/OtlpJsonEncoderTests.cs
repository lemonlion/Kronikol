using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// The encoder's oracle is the in-package receiver: every test encodes and decodes back with
/// <see cref="OtlpTraceReader"/>, plus a golden string that locks the byte-stable format in.
/// </summary>
public class OtlpJsonEncoderTests
{
    private static OtlpExportSpan Span(
        string traceId = "4bf92f3577b34da6a3ce929d0e0e4736",
        string spanId = "00f067aa0ba902b7",
        string name = "GET",
        string caller = "web",
        OtlpStatusCode status = OtlpStatusCode.Unset,
        string? statusMessage = null,
        OtlpSpanKind kind = OtlpSpanKind.Client,
        params OtlpExportAttribute[] attributes) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        Name = name,
        Kind = kind,
        StartTimeUnixNano = 1755820800000000000,
        EndTimeUnixNano = 1755820800012000000,
        ResourceServiceName = caller,
        Status = status,
        StatusMessage = statusMessage,
        Attributes = attributes,
    };

    [Fact]
    public void Encoded_spans_decode_back_with_the_reader()
    {
        var span = Span(attributes:
        [
            OtlpExportAttribute.Str("url.full", "http://api.example/things?q=1"),
            OtlpExportAttribute.Str("http.request.method", "GET"),
            OtlpExportAttribute.Int64("http.response.status_code", 200),
            OtlpExportAttribute.Boolean("kronikol.orphan", true),
        ]);

        var json = OtlpJsonEncoder.Encode([span]);
        var decoded = OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json));

        var read = Assert.Single(decoded);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", read.TraceId);
        Assert.Equal("00f067aa0ba902b7", read.SpanId);
        Assert.Null(read.ParentSpanId);
        Assert.Equal("GET", read.Name);
        Assert.Equal(OtlpSpanKind.Client, read.Kind);
        Assert.Equal(1755820800000000000UL, read.StartTimeUnixNano);
        Assert.Equal(1755820800012000000UL, read.EndTimeUnixNano);
        Assert.Equal("web", read.ServiceName);
        Assert.Equal(OtlpJsonEncoder.ScopeName, read.ScopeName);
        Assert.Equal("http://api.example/things?q=1", read.Attribute("url.full"));
        Assert.Equal("GET", read.Attribute("http.request.method"));
        Assert.Equal("200", read.Attribute("http.response.status_code"));
        Assert.Equal("true", read.Attribute("kronikol.orphan"));
        Assert.Equal(OtlpStatusCode.Unset, read.StatusCode);
    }

    [Fact]
    public void Error_status_and_message_round_trip()
    {
        var span = Span(status: OtlpStatusCode.Error, statusMessage: "Failed");

        var decoded = OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(OtlpJsonEncoder.Encode([span])));

        var read = Assert.Single(decoded);
        Assert.Equal(OtlpStatusCode.Error, read.StatusCode);
        Assert.Equal("Failed", read.StatusMessage);
    }

    [Fact]
    public void Producer_kind_round_trips()
    {
        var decoded = OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(OtlpJsonEncoder.Encode([Span(kind: OtlpSpanKind.Producer)])));
        Assert.Equal(OtlpSpanKind.Producer, Assert.Single(decoded).Kind);
    }

    [Fact]
    public void Spans_group_into_one_resource_entry_per_caller_in_first_appearance_order()
    {
        var spans = new[]
        {
            Span(spanId: "0000000000000001", caller: "web"),
            Span(spanId: "0000000000000002", caller: "api"),
            Span(spanId: "0000000000000003", caller: "web"),
        };

        var json = OtlpJsonEncoder.Encode(spans);
        var decoded = OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json));

        Assert.Equal(3, decoded.Count);
        // One resourceSpans entry per caller: "service.name":"web" appears once, "api" once.
        Assert.Equal(2, CountOf(json, "\"service.name\""));
        // Grouping gathers a caller's spans into its one resource entry (web's two spans, then api's).
        Assert.Equal(["web", "web", "api"], decoded.Select(s => s.ServiceName));
        // Grouping preserves per-resource span order.
        Assert.Equal(["0000000000000001", "0000000000000003"], decoded.Where(s => s.ServiceName == "web").Select(s => s.SpanId));
    }

    [Fact]
    public void Strings_are_json_escaped()
    {
        var span = Span(name: "quote \" backslash \\ newline \n tab \t control  unicode ✓",
            attributes: [OtlpExportAttribute.Str("kronikol.request.body", "{\"a\":\r\n1}")]);

        var json = OtlpJsonEncoder.Encode([span]);
        var decoded = OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json));

        var read = Assert.Single(decoded);
        Assert.Equal("quote \" backslash \\ newline \n tab \t control  unicode ✓", read.Name);
        Assert.Equal("{\"a\":\r\n1}", read.Attribute("kronikol.request.body"));
    }

    [Fact]
    public void Golden_format_is_byte_stable()
    {
        var span = Span(attributes:
        [
            OtlpExportAttribute.Str("url.full", "http://api.example/"),
            OtlpExportAttribute.Int64("http.response.status_code", 200),
            OtlpExportAttribute.Boolean("kronikol.orphan", true),
        ]);

        var json = OtlpJsonEncoder.Encode([span]);

        var expected =
            "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"web\"}}]}," +
            "\"scopeSpans\":[{\"scope\":{\"name\":\"Kronikol\",\"version\":\"" + OtlpJsonEncoder.ScopeVersion + "\"}," +
            "\"spans\":[{\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\",\"spanId\":\"00f067aa0ba902b7\"," +
            "\"name\":\"GET\",\"kind\":3,\"startTimeUnixNano\":\"1755820800000000000\",\"endTimeUnixNano\":\"1755820800012000000\"," +
            "\"attributes\":[{\"key\":\"url.full\",\"value\":{\"stringValue\":\"http://api.example/\"}}," +
            "{\"key\":\"http.response.status_code\",\"value\":{\"intValue\":\"200\"}}," +
            "{\"key\":\"kronikol.orphan\",\"value\":{\"boolValue\":true}}]}]}]}]}";
        Assert.Equal(expected, json);
    }

    [Fact]
    public void Golden_error_status_is_byte_stable()
    {
        var span = Span(status: OtlpStatusCode.Error, statusMessage: "Failed");

        var json = OtlpJsonEncoder.Encode([span]);

        Assert.EndsWith("\"attributes\":[],\"status\":{\"code\":2,\"message\":\"Failed\"}}]}]}]}", json);
    }

    [Fact]
    public void Empty_input_encodes_an_empty_document()
    {
        Assert.Equal("{\"resourceSpans\":[]}", OtlpJsonEncoder.Encode([]));
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
