using System.Text;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// Golden OTLP payloads, written by hand to the shapes real producers emit — the .NET
/// <c>MongoDB.Driver.Core.Extensions.DiagnosticSources</c> instrumentation and the Java agent (old
/// semconv), the Node <c>@opentelemetry/instrumentation-ioredis</c> auto-instrumentation, and the .NET
/// <c>HttpClient</c> instrumentation (stable semconv). These are the contract the mapper is tested
/// against; if a producer changes shape, the fix belongs here first.
/// </summary>
internal static class OtlpGoldens
{
    public const string TestTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    /// <summary>
    /// A .NET/Java Mongo client span with the <em>deprecated</em> conventions: <c>db.operation</c>,
    /// <c>db.mongodb.collection</c>, <c>db.name</c>, <c>net.peer.*</c>, <c>peer.service</c>, and no
    /// <c>db.statement</c> (the vendor package exposes no command-text switch). Span name
    /// <c>{collection}.{operation}</c>, as the Java agent writes it.
    /// </summary>
    public const string MongoFindOldSemconv = """
    {
      "resourceSpans": [{
        "resource": { "attributes": [
          { "key": "service.name", "value": { "stringValue": "data-insights" } },
          { "key": "deployment.environment", "value": { "stringValue": "Development" } }
        ]},
        "scopeSpans": [{
          "scope": { "name": "MongoDB.Driver.Core.Extensions.DiagnosticSources" },
          "spans": [{
            "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
            "spanId": "00f067aa0ba902b7",
            "parentSpanId": "a3ce929d0e0e4736",
            "name": "Trial.find",
            "kind": 3,
            "startTimeUnixNano": "1755820800000000000",
            "endTimeUnixNano": "1755820800012000000",
            "attributes": [
              { "key": "db.system", "value": { "stringValue": "mongodb" } },
              { "key": "db.operation", "value": { "stringValue": "find" } },
              { "key": "db.mongodb.collection", "value": { "stringValue": "Trial" } },
              { "key": "db.name", "value": { "stringValue": "data-insights-Development" } },
              { "key": "net.peer.name", "value": { "stringValue": "localhost" } },
              { "key": "net.peer.port", "value": { "intValue": "27099" } },
              { "key": "peer.service", "value": { "stringValue": "localhost:27099" } },
              { "key": "db.connection_id", "value": { "stringValue": "localhost:27099[connectionId:3]" } }
            ],
            "status": {}
          }]
        }]
      }]
    }
    """;

    /// <summary>
    /// A Node <c>@opentelemetry/instrumentation-ioredis</c> span: span name = the command, the statement
    /// carries the command and (unless values are elided) the key, and the database index rides on
    /// <c>db.redis.database_index</c>. <c>kind</c> arrives as the enum name here, which OTLP/JSON allows.
    /// </summary>
    public const string RedisGetIoredis = """
    {
      "resourceSpans": [{
        "resource": { "attributes": [
          { "key": "service.name", "value": { "stringValue": "superpay-graphql" } }
        ]},
        "scopeSpans": [{
          "scope": { "name": "@opentelemetry/instrumentation-ioredis" },
          "spans": [{
            "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
            "spanId": "1111111111111111",
            "name": "get",
            "kind": "SPAN_KIND_CLIENT",
            "startTimeUnixNano": "1755820800100000000",
            "endTimeUnixNano": "1755820800101500000",
            "attributes": [
              { "key": "db.system", "value": { "stringValue": "redis" } },
              { "key": "db.statement", "value": { "stringValue": "get insights:trials:2026-08-22" } },
              { "key": "db.redis.database_index", "value": { "intValue": "0" } },
              { "key": "net.peer.name", "value": { "stringValue": "localhost" } },
              { "key": "net.peer.port", "value": { "intValue": "6399" } }
            ],
            "status": { "code": 0 }
          }]
        }]
      }]
    }
    """;

    /// <summary>A .NET <c>HttpClient</c> client span on the stable conventions, plus a server span for the same hop that must be ignored.</summary>
    public const string HttpClientAndServer = """
    {
      "resourceSpans": [{
        "resource": { "attributes": [
          { "key": "service.name", "value": { "stringValue": "data-insights" } }
        ]},
        "scopeSpans": [{
          "scope": { "name": "System.Net.Http" },
          "spans": [
            {
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
              "spanId": "2222222222222222",
              "name": "GET",
              "kind": 3,
              "startTimeUnixNano": "1755820800200000000",
              "endTimeUnixNano": "1755820800450000000",
              "attributes": [
                { "key": "http.request.method", "value": { "stringValue": "GET" } },
                { "key": "url.full", "value": { "stringValue": "http://localhost:9000/api/summary?trial=42" } },
                { "key": "server.address", "value": { "stringValue": "localhost" } },
                { "key": "server.port", "value": { "intValue": "9000" } },
                { "key": "http.response.status_code", "value": { "intValue": "200" } }
              ],
              "status": {}
            },
            {
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
              "spanId": "3333333333333333",
              "name": "GET /api/summary",
              "kind": 2,
              "startTimeUnixNano": "1755820800210000000",
              "endTimeUnixNano": "1755820800440000000",
              "attributes": [
                { "key": "http.request.method", "value": { "stringValue": "GET" } },
                { "key": "url.path", "value": { "stringValue": "/api/summary" } },
                { "key": "http.response.status_code", "value": { "intValue": "200" } },
                { "key": "client.address", "value": { "stringValue": "127.0.0.1" } }
              ],
              "status": {}
            }
          ]
        }]
      }]
    }
    """;

    /// <summary>A BigQuery client span that failed, so the mapper has to derive <c>500</c> and the status message.</summary>
    public const string BigQueryFailed = """
    {
      "resourceSpans": [{
        "resource": { "attributes": [
          { "key": "service.name", "value": { "stringValue": "data-insights" } }
        ]},
        "scopeSpans": [{
          "spans": [{
            "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
            "spanId": "4444444444444444",
            "name": "BigQueryRepository.ExecuteQuery",
            "kind": 3,
            "startTimeUnixNano": "1755820800300000000",
            "endTimeUnixNano": "1755820800900000000",
            "attributes": [
              { "key": "db.system.name", "value": { "stringValue": "bigquery" } },
              { "key": "db.query.text", "value": { "stringValue": "SELECT trial_id FROM `insights.trials` LIMIT 10" } },
              { "key": "db.namespace", "value": { "stringValue": "insights" } },
              { "key": "server.address", "value": { "stringValue": "bq-emulator" } }
            ],
            "status": { "code": 2, "message": "Deadline exceeded" }
          }]
        }]
      }]
    }
    """;

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ------------------------------------------------------------------ protobuf builder

    /// <summary>Builds the protobuf encoding of a one-span <c>ExportTraceServiceRequest</c> — the same span as <see cref="MongoFindOldSemconv"/>.</summary>
    public static byte[] MongoFindProtobuf()
    {
        var span = Concat(
            LenField(1, FromHex("4bf92f3577b34da6a3ce929d0e0e4736")),   // trace_id
            LenField(2, FromHex("00f067aa0ba902b7")),                   // span_id
            LenField(4, FromHex("a3ce929d0e0e4736")),                   // parent_span_id
            StrField(5, "Trial.find"),                                  // name
            VarintField(6, 3),                                          // kind = CLIENT
            Fixed64Field(7, 1_755_820_800_000_000_000),                 // start
            Fixed64Field(8, 1_755_820_800_012_000_000),                 // end
            LenField(9, StringKeyValue("db.system", "mongodb")),
            LenField(9, StringKeyValue("db.operation", "find")),
            LenField(9, StringKeyValue("db.mongodb.collection", "Trial")),
            LenField(9, StringKeyValue("db.name", "data-insights-Development")),
            LenField(9, StringKeyValue("net.peer.name", "localhost")),
            LenField(9, IntKeyValue("net.peer.port", 27099)),
            LenField(9, StringKeyValue("peer.service", "localhost:27099")));

        var scopeSpans = Concat(
            LenField(1, StrField(1, "MongoDB.Driver.Core.Extensions.DiagnosticSources")), // scope { name }
            LenField(2, span));

        var resource = LenField(1, StringKeyValue("service.name", "data-insights"));

        var resourceSpans = Concat(
            LenField(1, resource),
            LenField(2, scopeSpans));

        return LenField(1, resourceSpans);
    }

    /// <summary>A protobuf span with a <c>double</c>, a <c>bool</c> and an array attribute, to exercise every <c>AnyValue</c> branch.</summary>
    public static byte[] AttributeShapesProtobuf()
    {
        var array = Concat(LenField(1, StrField(1, "a")), LenField(1, StrField(1, "b")));
        var span = Concat(
            LenField(1, FromHex(TestTraceId)),
            LenField(2, FromHex("5555555555555555")),
            StrField(5, "shapes"),
            VarintField(6, 3),
            Fixed64Field(7, 1_755_820_800_000_000_000),
            Fixed64Field(8, 1_755_820_800_001_000_000),
            LenField(9, StringKeyValue("db.system", "postgresql")),
            LenField(9, StringKeyValue("db.query.text", "SELECT 1")),
            LenField(9, KeyValue("cache.hit", VarintField(2, 1))),
            LenField(9, KeyValue("ratio", Fixed64Field(4, (ulong)BitConverter.DoubleToInt64Bits(0.5)))),
            LenField(9, KeyValue("tags", LenField(5, array))),
            LenField(15, Concat(StrField(2, "fine"), VarintField(3, 1))));

        var scopeSpans = LenField(2, span);
        var resourceSpans = Concat(LenField(1, LenField(1, StringKeyValue("service.name", "svc"))), LenField(2, scopeSpans));
        return LenField(1, resourceSpans);
    }

    public static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>(10);
        do
        {
            var b = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            bytes.Add(b);
        }
        while (value != 0);

        return bytes.ToArray();
    }

    public static byte[] LenField(int field, byte[] payload) => Concat(Varint((ulong)((field << 3) | 2)), Varint((ulong)payload.Length), payload);

    public static byte[] StrField(int field, string value) => LenField(field, Encoding.UTF8.GetBytes(value));

    public static byte[] VarintField(int field, ulong value) => Concat(Varint((ulong)(field << 3)), Varint(value));

    public static byte[] Fixed64Field(int field, ulong value) => Concat(Varint((ulong)((field << 3) | 1)), BitConverter.GetBytes(value));

    public static byte[] KeyValue(string key, byte[] anyValue) => Concat(StrField(1, key), LenField(2, anyValue));

    public static byte[] StringKeyValue(string key, string value) => KeyValue(key, StrField(1, value));

    public static byte[] IntKeyValue(string key, long value) => KeyValue(key, VarintField(3, (ulong)value));

    public static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
