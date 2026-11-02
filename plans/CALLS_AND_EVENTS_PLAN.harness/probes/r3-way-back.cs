// The way back through SpanToInteractionMapper, on R3's first cut: OTLP spans in the shapes real producers write, each read as a
// collector would be sent it, then mapped, then exported again. The shapes are written here to the semantic conventions'
// attributes (v1.44.0, and its deprecated names, which Java agents and older SDKs still write); r3-real-capture.cs holds spans a
// real instrumentation wrote. It answers: which shapes come back as Event, which are dropped, and what kind a span that came in
// goes out as.
//
// Published-package lines, so that it builds as the rest of this harness does, but it says nothing of 5.27.13: run it with on-prototype.sh (see r3-span-kinds.cs).
//
// Usage, from the harness directory: probes/on-prototype.sh <worktree> probes/r3-way-back.cs <scratch> > results/r3-way-back.txt
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false

using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

var shapes = new (string Name, OtlpSpanKind Kind, (string Key, string Value)[] Attributes)[]
{
    ("send, stable names", OtlpSpanKind.Producer, [("messaging.system", "kafka"), ("messaging.operation.name", "send"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("publish, old names (Java agent)", OtlpSpanKind.Producer, [("messaging.system", "kafka"), ("messaging.operation", "publish"), ("messaging.destination", "orders")]),
    ("create (batch publish)", OtlpSpanKind.Producer, [("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "create"), ("messaging.operation.type", "create"), ("messaging.destination.name", "orders")]),
    ("send as CLIENT (a create span exists)", OtlpSpanKind.Client, [("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "publish"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("process", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.operation.name", "process"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
    ("receive as CONSUMER (old semconv)", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.operation", "receive"), ("messaging.destination", "orders")]),
    ("CONSUMER, no operation", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.destination.name", "orders")]),
    ("poll (.NET Kafka instrumentation)", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.operation.name", "poll"), ("messaging.operation.type", "receive"), ("messaging.destination.name", "orders")]),
    ("commit (settle)", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.operation.name", "commit"), ("messaging.operation.type", "settle"), ("messaging.destination.name", "orders")]),
    ("CLIENT, no operation", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.destination.name", "orders")]),
    ("receive with rpc.system (AWS SDK shape)", OtlpSpanKind.Client, [("messaging.system", "aws_sqs"), ("rpc.system", "aws-api"), ("rpc.service", "SQS"), ("rpc.method", "ReceiveMessage"), ("messaging.destination.name", "orders")]),
    ("send with rpc.system (AWS SDK shape)", OtlpSpanKind.Producer, [("messaging.system", "aws_sqs"), ("rpc.system", "aws-api"), ("rpc.service", "SQS"), ("rpc.method", "SendMessage"), ("messaging.destination.name", "orders")]),
    ("messaging.system.name only (semconv main)", OtlpSpanKind.Producer, [("messaging.system.name", "kafka"), ("messaging.operation.name", "send"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("INTERNAL with messaging.system", OtlpSpanKind.Internal, [("messaging.system", "kafka"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
    ("PRODUCER, no messaging.system (today's export of a message)", OtlpSpanKind.Producer, [("url.full", "pubsub:///orders"), ("peer.service", "PubSub"), ("kronikol.dependency.category", "MessageQueue")]),
    ("CONSUMER, no messaging.system, nothing else", OtlpSpanKind.Consumer, []),
    ("CONSUMER with http.request.method (a push over HTTP)", OtlpSpanKind.Consumer, [("http.request.method", "POST"), ("url.full", "http://app/push")]),
    ("PRODUCER with db.system.name (Spanner as exported to 5.27.13)", OtlpSpanKind.Producer, [("db.system.name", "spanner"), ("db.namespace", "shop"), ("db.operation.name", "SELECT")]),
    ("CONSUMER with db.system", OtlpSpanKind.Consumer, [("db.system", "redis"), ("db.statement", "get k")]),
    ("Pub/Sub's process (the page names its operation subscribe)", OtlpSpanKind.Consumer, [("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "subscribe"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
};

var byDefault = new OtlpTapOptions { ListenPort = 1 };
var captured = new OtlpTapOptions { ListenPort = 1 };
captured.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
captured.CaptureKinds.Add(OtlpCaptureKinds.Rpc);
var reexport = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces"), IncludeSpanSourced = true };
var echoSuppressed = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };

Console.WriteLine("shape\tkind in\tdefault CaptureKinds\twith Messaging+Rpc: metaType\tlabel\turi\tcaller\tservice\tNDJSON metaType\tkind out (IncludeSpanSourced)\tservice.name out\tpeer.service out\tby default on the way out");
foreach (var (name, kind, attributes) in shapes)
{
    var span = Read(kind, attributes);
    var mappedByDefault = SpanToInteractionMapper.Map(span, byDefault);
    var mapped = SpanToInteractionMapper.Map(span, captured);
    if (mapped is null)
    {
        Console.WriteLine($"{name}\t{kind}\t{(mappedByDefault is null ? "dropped" : "mapped")}\tDROPPED");
        continue;
    }

    var (request, response) = mapped.ToLogs();
    var (record, _) = mapped.ToRecords();
    var outward = OtlpSpanMapper.Map(request, response, reexport, DateTimeOffset.UtcNow);
    Console.WriteLine(string.Join('\t',
        name, kind, mappedByDefault is null ? "dropped" : "mapped", request.MetaType, mapped.Method, mapped.Uri, mapped.CallerName, mapped.ServiceName,
        record.MetaType ?? "(none)", outward.Kind, outward.ResourceServiceName, outward.Attribute("peer.service"), OtlpSpanMapper.ShouldSkip(request, echoSuppressed) ? "skipped (echo)" : "exported"));
}

static OtlpSpan Read(OtlpSpanKind kind, (string Key, string Value)[] attributes)
{
    var values = string.Join(',', attributes.Select(a => "{\"key\":\"" + a.Key + "\",\"value\":{\"stringValue\":\"" + a.Value + "\"}}"));
    var json = "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"app\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"probe\"},\"spans\":[{"
        + "\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\",\"spanId\":\"00f067aa0ba902b7\",\"name\":\"probe\",\"kind\":" + (int)kind
        + ",\"startTimeUnixNano\":\"1755820800000000000\",\"endTimeUnixNano\":\"1755820800012000000\",\"attributes\":[" + values + "]}]}]}]}";
    return OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json)).Single();
}
