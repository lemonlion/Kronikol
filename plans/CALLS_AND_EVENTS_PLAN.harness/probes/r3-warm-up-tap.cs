// What F11 does to warm-up: spans a Messaging-on tap captures, whose record R3 writes as Event where 5.27.13 writes Default. WarmUpCalls
// skips an Event (WarmUpCalls.cs:155), and Q5's condition judges one only where MessageTable reads it as Publishes. Each row is one shape
// of call: a slow first span and two quick ones, in one scenario, as a real instrumentation would send them. Its caller is a worker nothing
// in the scenario called, so the actor rule keeps it in, or the test's own caller. Prints whether the first call gets its mark.
//
// Usage, from the harness directory:
//   dotnet run --file probes/r3-warm-up-tap.cs > results/r3-warm-up-tap-5.27.13.txt
//   probes/on-prototype.sh <worktree> probes/r3-warm-up-tap.cs <scratch> > results/r3-warm-up-tap.txt
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false

using System.Collections;
using System.Reflection;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Reports;
using Kronikol.Tracking;

var options = new OtlpTapOptions { ListenPort = 1 };
options.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
var core = typeof(RequestResponseLog).Assembly;
var find = core.GetType("Kronikol.Reports.WarmUpCalls", throwOnError: true)!.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!;
var read = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;

var scenario = new Scenario { Id = "s1", DisplayName = "s1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(5) };
Feature[] features = [new Feature { DisplayName = "f", Scenarios = [scenario] }];

(string Key, string Value)[] Messaging(string system, string name, string? type, string destination = "orders") =>
    type is null
        ? [("messaging.system", system), ("messaging.operation.name", name), ("messaging.destination.name", destination)]
        : [("messaging.system", system), ("messaging.operation.name", name), ("messaging.operation.type", type), ("messaging.destination.name", destination)];

Console.WriteLine("shape\tspan kind\tlabel\turi\tmetaType\tMessageTable.Read\tmarks on the first call");

void Row(string name, OtlpSpanKind kind, (string Key, string Value)[] attributes)
{
    var logs = new List<RequestResponseLog?>();
    string label = "", uri = "", metaType = "";
    for (var i = 0; i < 3; i++)
    {
        var start = 1755820800000000000L + (i == 0 ? 0 : (700L + 100L * i) * 1_000_000L);
        var duration = (i == 0 ? 600L : 5L) * 1_000_000L;
        var mapped = SpanToInteractionMapper.Map(Span(kind, attributes, $"{i + 1:x16}", start, start + duration), options);
        if (mapped is null)
        {
            Console.WriteLine(string.Join('\t', name, kind, "-", "-", "-", "-", "not captured"));
            return;
        }

        var (request, response) = mapped.ToLogs();
        logs.Add(request with { TestId = "s1", TestName = "s1" });
        logs.Add(response with { TestId = "s1", TestName = "s1" });
        (label, uri, metaType) = (request.Method.Value?.ToString() ?? "", request.Uri.ToString(), request.MetaType.ToString());
    }

    var result = find.Invoke(null, [features, logs, null])!;
    var calls = (ICollection)result.GetType().GetProperty("Calls")!.GetValue(result)!;
    var marks = new List<string>();
    foreach (var entry in calls)
    {
        var mark = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        marks.Add(mark.GetType().GetProperty("Kind")!.GetValue(mark)!.ToString()!);
    }

    var verdict = read.Invoke(null, [new Uri(uri).Scheme, label])!;
    Console.WriteLine(string.Join('\t', name, kind, label, uri, metaType, verdict, marks.Count == 0 ? "none" : string.Join(",", marks)));
}

Row("an uncalled worker's Kafka send", OtlpSpanKind.Producer, Messaging("kafka", "send", "send"));
Row("an uncalled worker's Kafka publish, old name", OtlpSpanKind.Producer, [("messaging.system", "kafka"), ("messaging.operation", "publish"), ("messaging.destination.name", "orders")]);
Row("an uncalled worker's Service Bus send", OtlpSpanKind.Producer, Messaging("servicebus", "send", "send"));
Row("an uncalled worker's Event Hubs send", OtlpSpanKind.Producer, Messaging("eventhubs", "send", "send"));
Row("an uncalled worker's Pub/Sub publish (create)", OtlpSpanKind.Producer, Messaging("gcp_pubsub", "create", "create"));
Row("an uncalled worker's Pub/Sub publish (send, CLIENT)", OtlpSpanKind.Client, Messaging("gcp_pubsub", "publish", "send"));
Row("an uncalled worker's SQS send", OtlpSpanKind.Producer, Messaging("aws_sqs", "send", "send"));
Row("an uncalled worker's SNS publish", OtlpSpanKind.Producer, Messaging("aws.sns", "publish", "send"));
Row("an uncalled worker's RabbitMQ publish", OtlpSpanKind.Producer, Messaging("rabbitmq", "publish", "send"));
Row("an uncalled worker's MassTransit send (in memory)", OtlpSpanKind.Producer, [("messaging.system", "in-memory"), ("messaging.operation", "send"), ("messaging.destination.name", "urn:message:Probe:OrderPlaced")]);
Row("a Kafka poll (long: the first waits for a message)", OtlpSpanKind.Client, Messaging("kafka", "poll", "receive"));
Row("a Kafka process span", OtlpSpanKind.Consumer, Messaging("kafka", "process", "process"));
Row("a Kafka commit (settle)", OtlpSpanKind.Client, Messaging("kafka", "commit", "settle"));
Row("control: an HTTP span by the same worker", OtlpSpanKind.Client, [("http.request.method", "GET"), ("url.full", "http://stock.example/items"), ("server.address", "stock.example")]);
Row("control: a database span by the same worker", OtlpSpanKind.Client, [("db.system.name", "postgresql"), ("db.namespace", "shop"), ("db.query.text", "SELECT 1")]);

static OtlpSpan Span(OtlpSpanKind kind, (string Key, string Value)[] attributes, string spanId, long start, long end)
{
    var values = string.Join(',', attributes.Select(a => "{\"key\":\"" + a.Key + "\",\"value\":{\"stringValue\":\"" + a.Value + "\"}}"));
    var json = "{\"resourceSpans\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"worker\"}}]},\"scopeSpans\":[{\"scope\":{\"name\":\"probe\"},\"spans\":[{"
        + "\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\",\"spanId\":\"" + spanId + "\",\"name\":\"probe\",\"kind\":" + (int)kind
        + ",\"startTimeUnixNano\":\"" + start + "\",\"endTimeUnixNano\":\"" + end + "\",\"attributes\":[" + values + "]}]}]}]}";
    return OtlpTraceReader.ReadJson(Encoding.UTF8.GetBytes(json)).Single();
}
