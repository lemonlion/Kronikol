// Whether R3's tap can move a history fingerprint. Each span, as a hand-made shape or as the real instrumentations wrote it (the protobuf
// saved by r3-real-capture.cs), goes through the tap's mapper, both ways a tap record reaches history (the log, and the NDJSON record read
// back) and through InteractionMerger with a wire twin. For each it prints what InteractionShape makes of the record: the calls, under the
// current rule and every kept one; the raw call a fragment carries; the fingerprints; the dependencies. Run on 5.27.13 and on R3 and diff:
// a line that differs is a line history moves on. Lines starting "kind " are the record's kind, which the fingerprint does not read, and
// are there so that the diff shows the change it is looking past.
//
// Usage, from the harness directory:
//   dotnet run --file probes/r3-fingerprint.cs -- results/r3-real-capture.payloads.txt > results/r3-fingerprint-5.27.13.txt
//   probes/on-prototype.sh <worktree> probes/r3-fingerprint.cs <scratch> results/r3-real-capture.payloads.txt > results/r3-fingerprint.txt
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false

using System.Collections;
using System.Reflection;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.History;
using Kronikol.Ingestion;
using Kronikol.Tracking;

var shapes = new (string Name, OtlpSpanKind Kind, (string Key, string Value)[] Attributes)[]
{
    ("send, stable names", OtlpSpanKind.Producer, [("messaging.system", "kafka"), ("messaging.operation.name", "send"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("publish, old names (Java agent)", OtlpSpanKind.Producer, [("messaging.system", "kafka"), ("messaging.operation", "publish"), ("messaging.destination.name", "orders")]),
    ("create (batch publish)", OtlpSpanKind.Producer, [("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "create"), ("messaging.operation.type", "create"), ("messaging.destination.name", "orders")]),
    ("send as CLIENT (a create span exists)", OtlpSpanKind.Client, [("messaging.system", "gcp_pubsub"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("process", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.operation.name", "process"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
    ("Pub/Sub's process (subscribe)", OtlpSpanKind.Consumer, [("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "subscribe"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
    ("receive as CONSUMER (old semconv)", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.operation", "receive"), ("messaging.destination.name", "orders")]),
    ("CONSUMER, no operation", OtlpSpanKind.Consumer, [("messaging.system", "kafka"), ("messaging.destination.name", "orders")]),
    ("poll (.NET Kafka instrumentation)", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.operation.name", "poll"), ("messaging.operation.type", "receive"), ("messaging.destination.name", "orders")]),
    ("commit (settle)", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.operation.name", "commit"), ("messaging.operation.type", "settle"), ("messaging.destination.name", "orders")]),
    ("CLIENT, no operation", OtlpSpanKind.Client, [("messaging.system", "kafka"), ("messaging.destination.name", "orders")]),
    ("receive with rpc.system (AWS SDK shape)", OtlpSpanKind.Client, [("messaging.system", "aws_sqs"), ("rpc.system", "aws-api"), ("rpc.service", "SQS"), ("rpc.method", "ReceiveMessage")]),
    ("send with rpc.system (AWS SDK shape)", OtlpSpanKind.Producer, [("messaging.system", "aws_sqs"), ("rpc.system", "aws-api"), ("rpc.service", "SQS"), ("rpc.method", "SendMessage"), ("messaging.destination.name", "orders")]),
    ("INTERNAL with messaging.system", OtlpSpanKind.Internal, [("messaging.system", "kafka"), ("messaging.operation.type", "process"), ("messaging.destination.name", "orders")]),
    // Main of the conventions' name for the system, which v1.44.0 does not have: R3 reads it no more than 5.27.13 does.
    ("messaging.system.name only, PRODUCER", OtlpSpanKind.Producer, [("messaging.system.name", "kafka"), ("messaging.destination.name", "orders")]),
    ("messaging.system.name only, INTERNAL", OtlpSpanKind.Internal, [("messaging.system.name", "kafka"), ("messaging.destination.name", "orders")]),
    ("both names", OtlpSpanKind.Producer, [("messaging.system.name", "aws.sqs"), ("messaging.system", "aws_sqs"), ("messaging.destination.name", "orders")]),
    ("PRODUCER, no messaging.system: what 5.27.13 exports for a message", OtlpSpanKind.Producer, [("url.full", "pubsub:///orders"), ("peer.service", "PubSub"), ("kronikol.dependency.category", "MessageQueue")]),
    ("PRODUCER, messaging.*: what R3 exports for the same message", OtlpSpanKind.Producer, [("url.full", "pubsub:///orders"), ("peer.service", "PubSub"), ("kronikol.dependency.category", "MessageQueue"), ("messaging.system", "gcp_pubsub"), ("messaging.operation.name", "Publish"), ("messaging.operation.type", "send"), ("messaging.destination.name", "orders")]),
    ("CONSUMER, no messaging.system, nothing else", OtlpSpanKind.Consumer, []),
    ("CONSUMER with http.request.method (a push over HTTP)", OtlpSpanKind.Consumer, [("http.request.method", "POST"), ("url.full", "http://app/push")]),
    ("PRODUCER with db.system.name (Spanner as exported to 5.27.13)", OtlpSpanKind.Producer, [("db.system.name", "spanner"), ("db.namespace", "shop"), ("db.operation.name", "SELECT")]),
    ("CONSUMER with db.system", OtlpSpanKind.Consumer, [("db.system", "redis"), ("db.statement", "get k")]),
};

var byDefault = new OtlpTapOptions { ListenPort = 1 };
var captured = new OtlpTapOptions { ListenPort = 1 };
captured.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
captured.CaptureKinds.Add(OtlpCaptureKinds.Rpc);

var shape = typeof(InteractionShape);
var keptVersions = (IEnumerable<int>)shape.GetProperty("KeptVersions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var callsUnder = shape.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "Calls" && m.GetParameters() is [var a, _, { ParameterType: var v }] && v == typeof(int) && a.ParameterType.GetGenericArguments() is [var t] && t == typeof(RequestResponseLog));
var rawCalls = shape.GetMethod("RawCalls", BindingFlags.Static | BindingFlags.NonPublic)!;

void Report(string prefix, RequestResponseLog request, RequestResponseLog response)
{
    RequestResponseLog?[] logs = [request, response];
    Console.WriteLine($"{prefix}\tkind {request.MetaType}");
    var calls = InteractionShape.Calls(logs);
    foreach (var call in calls)
        Console.WriteLine($"{prefix}\tcall {call}");
    foreach (var version in keptVersions)
        foreach (var call in (IEnumerable)callsUnder.Invoke(null, [logs, null, version])!)
            Console.WriteLine($"{prefix}\tunder {version} {call}");
    foreach (var raw in (IEnumerable)rawCalls.Invoke(null, [logs])!)
        Console.WriteLine($"{prefix}\traw {raw}");
    var (set, ordered, count) = InteractionShape.Fingerprint(calls);
    Console.WriteLine($"{prefix}\tfingerprint set={set} ordered={ordered} calls={count}");
    Console.WriteLine($"{prefix}\tdependencies {string.Join(",", InteractionShape.Dependencies(logs))}");
}

void Through(string name, OtlpSpan span, OtlpTapOptions options, string taken)
{
    var prefix = $"{name} | {taken}";
    var mapped = SpanToInteractionMapper.Map(span, options);
    if (mapped is null)
    {
        Console.WriteLine($"{prefix}\tdropped");
        return;
    }

    var (request, response) = mapped.ToLogs();
    Report($"{prefix} | log", request, response);

    var (requestRecord, responseRecord) = mapped.ToRecords();
    Report($"{prefix} | record", requestRecord.ToLog(), responseRecord.ToLog());

    // A wire capturer saw the same call: the same parties, label and address, over the same time, with no kind of its own.
    var wireId = Guid.NewGuid().ToString();
    var wireRequest = requestRecord with { CapturedBy = InteractionMerger.WireSource, MetaType = null, RequestResponseId = wireId, TraceId = wireId };
    var wireResponse = responseRecord with { CapturedBy = InteractionMerger.WireSource, MetaType = null, RequestResponseId = wireId, TraceId = wireId };
    var merged = InteractionMerger.Merge([wireRequest, wireResponse, requestRecord, responseRecord]);
    Console.WriteLine($"{prefix} | merged\trecords {merged.Count}, captured by {string.Join("+", merged.Select(r => r.CapturedBy).Distinct())}");
    if (merged.Count == 2)
        Report($"{prefix} | merged", merged[0].ToLog(), merged[1].ToLog());
}

foreach (var (name, kind, attributes) in shapes)
{
    var span = Read(kind, attributes);
    Through(name, span, byDefault, "default capture");
    Through(name, span, captured, "Messaging and Rpc captured");
}

// The bytes the real instrumentations wrote.
if (args.Length > 0)
{
    var post = 0;
    foreach (var line in File.ReadAllLines(args[0]))
    {
        post++;
        var spanNumber = 0;
        foreach (var span in OtlpTraceReader.Read(Convert.FromBase64String(line), "application/x-protobuf"))
        {
            spanNumber++;
            var name = $"real POST {post} span {spanNumber} {span.Kind} {span.Name}";
            Through(name, span, byDefault, "default capture");
            Through(name, span, captured, "Messaging and Rpc captured");
        }
    }
}

static OtlpSpan Read(OtlpSpanKind kind, (string Key, string Value)[] attributes)
{
    var values = string.Join(',', attributes.Select(a => "{\"key\":\"" + a.Key + "\",\"value\":{\"stringValue\":\"" + a.Value + "\"}}"));
    var json = "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"app\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"probe\"},\"spans\":[{"
        + "\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\",\"spanId\":\"00f067aa0ba902b7\",\"name\":\"probe\",\"kind\":" + (int)kind
        + ",\"startTimeUnixNano\":\"1755820800000000000\",\"endTimeUnixNano\":\"1755820800012000000\",\"attributes\":[" + values + "]}]}]}]}";
    return OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json)).Single();
}
