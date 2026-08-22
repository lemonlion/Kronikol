using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

public class OtlpTraceReaderTests
{
    [Fact]
    public void Reads_the_json_encoding_with_hex_ids_string_nanos_and_a_numeric_kind()
    {
        var span = Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv)));

        Assert.Equal(OtlpGoldens.TestTraceId, span.TraceId);
        Assert.Equal("00f067aa0ba902b7", span.SpanId);
        Assert.Equal("a3ce929d0e0e4736", span.ParentSpanId);
        Assert.Equal("Trial.find", span.Name);
        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal(1_755_820_800_000_000_000UL, span.StartTimeUnixNano);
        Assert.Equal("data-insights", span.ServiceName);
        Assert.Equal("MongoDB.Driver.Core.Extensions.DiagnosticSources", span.ScopeName);
        Assert.Equal("mongodb", span.Attribute("db.system"));
        Assert.Equal("27099", span.Attribute("net.peer.port"));
        Assert.Equal(OtlpStatusCode.Unset, span.StatusCode);
    }

    [Fact]
    public void Reads_an_enum_named_kind_and_a_status_code()
    {
        var redis = Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.RedisGetIoredis)));
        Assert.Equal(OtlpSpanKind.Client, redis.Kind);

        var failed = Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.BigQueryFailed)));
        Assert.Equal(OtlpStatusCode.Error, failed.StatusCode);
        Assert.Equal("Deadline exceeded", failed.StatusMessage);
    }

    [Fact]
    public void Accepts_base64_ids_from_producers_that_use_the_stock_protobuf_json_mapping()
    {
        var json = """
        {"resourceSpans":[{"resource":{"attributes":[]},"scopeSpans":[{"spans":[{
          "traceId":"S/kvNXezTaajzpKdDg5HNg==","spanId":"APBnqgupArc=","name":"x","kind":3,
          "startTimeUnixNano":"1","endTimeUnixNano":"2"}]}]}]}
        """;

        var span = Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(json)));
        Assert.Equal(OtlpGoldens.TestTraceId, span.TraceId);
        Assert.Equal("00f067aa0ba902b7", span.SpanId);
    }

    [Fact]
    public void Reads_the_protobuf_encoding_to_the_same_span_as_the_json_one()
    {
        var fromProtobuf = Assert.Single(OtlpTraceReader.ReadProtobuf(OtlpGoldens.MongoFindProtobuf()));
        var fromJson = Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv)));

        Assert.Equal(fromJson.TraceId, fromProtobuf.TraceId);
        Assert.Equal(fromJson.SpanId, fromProtobuf.SpanId);
        Assert.Equal(fromJson.ParentSpanId, fromProtobuf.ParentSpanId);
        Assert.Equal(fromJson.Name, fromProtobuf.Name);
        Assert.Equal(fromJson.Kind, fromProtobuf.Kind);
        Assert.Equal(fromJson.StartTimeUnixNano, fromProtobuf.StartTimeUnixNano);
        Assert.Equal(fromJson.EndTimeUnixNano, fromProtobuf.EndTimeUnixNano);
        Assert.Equal(fromJson.ServiceName, fromProtobuf.ServiceName);
        Assert.Equal(fromJson.ScopeName, fromProtobuf.ScopeName);
        foreach (var key in new[] { "db.system", "db.operation", "db.mongodb.collection", "db.name", "net.peer.name", "net.peer.port", "peer.service" })
            Assert.Equal(fromJson.Attribute(key), fromProtobuf.Attribute(key));
    }

    [Fact]
    public void Reads_every_protobuf_attribute_shape()
    {
        var span = Assert.Single(OtlpTraceReader.ReadProtobuf(OtlpGoldens.AttributeShapesProtobuf()));

        Assert.Equal("true", span.Attribute("cache.hit"));
        Assert.Equal("0.5", span.Attribute("ratio"));
        Assert.Equal("[a,b]", span.Attribute("tags"));
        Assert.Equal(OtlpStatusCode.Ok, span.StatusCode);
        Assert.Equal("fine", span.StatusMessage);
    }

    [Fact]
    public void Chooses_the_encoding_from_the_content_type_and_sniffs_json_when_there_is_none()
    {
        Assert.Single(OtlpTraceReader.Read(OtlpGoldens.MongoFindProtobuf(), "application/x-protobuf"));
        Assert.Single(OtlpTraceReader.Read(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), "application/json"));
        Assert.Single(OtlpTraceReader.Read(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv), null));
    }

    [Fact]
    public void Malformed_input_yields_no_spans_instead_of_throwing()
    {
        Assert.Empty(OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes("{ not json")));
        Assert.Empty(OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes("{}")));
        Assert.Empty(OtlpTraceReader.ReadProtobuf([0xff, 0xff, 0xff]));
        Assert.Empty(OtlpTraceReader.ReadProtobuf([]));
    }
}
