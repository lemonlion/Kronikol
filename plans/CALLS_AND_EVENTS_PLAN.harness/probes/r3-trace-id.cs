// F15 on R3's first cut, and what the pair key does beside it: does one call stay one span? Each row drives the real MessageTracker
// or an NDJSON record through OtlpSpanMapper.MapAll, the exporter's path. Also two collisions the plan does not name: calls
// under one Activity, and a Kafka produce and the consume of the same message, whose exported spans share a span id.
//
// Usage, from the harness directory: probes/on-prototype.sh <worktree> probes/r3-trace-id.cs <scratch> > results/r3-trace-id.txt
// results/r3-trace-id-5.27.13.txt is this probe run as it stands, on the published packages: the behaviour before R3.
// results/r3-trace-id-keyed-on-pair-id.txt is the first cut with OtlpSpanMapper and OtlpExportSink pairing on RequestResponseId alone.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:property PublishAot=false

using System.Diagnostics;
using System.Reflection;
using System.Text;
using Confluent.Kafka;
using Kronikol.Constants;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Http;

var otlp = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };
var perPair = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces"), TraceIdStrategy = TraceIdStrategy.PerPair };
var currentTest = "";
(string, string) Current() => ("r3-trace-id", currentTest);
var uri = new Uri("event://cake_events");

RequestResponseLog[] Logs(Action call)
{
    currentTest = Guid.NewGuid().ToString("N");
    call();
    return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
}

void Report(string name, RequestResponseLog[] logs)
{
    var requests = logs.Where(l => l.Type == RequestResponseType.Request).ToArray();
    var responses = logs.Where(l => l.Type == RequestResponseType.Response).ToArray();
    var same = requests.Length == 1 && responses.Length == 1 ? (requests[0].TraceId == responses[0].TraceId ? "same" : "DIFFERS") : "-";
    var batch = OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow);
    var pairBatch = OtlpSpanMapper.MapAll(logs, perPair, DateTimeOffset.UtcNow);
    var ids = batch.Spans.Select(s => (s.TraceId, s.SpanId)).Distinct().Count();
    Console.WriteLine($"{name}\t{requests.Length} request(s), {responses.Length} response(s)\tpair's trace ids {same}\tPerTest: {batch.Spans.Count} span(s), {batch.OrphanSpans} orphan(s), {ids} distinct (trace, span)\tPerPair: {pairBatch.Spans.Count} span(s), {pairBatch.OrphanSpans} orphan(s)\tkinds {string.Join(",", batch.Spans.Select(s => s.Kind))}");
}

Console.WriteLine("-- MessageTracker, no HTTP context");
var tracker = new MessageTracker(new MessageTrackerOptions { ServiceName = "Broker", CallerName = "App", CurrentTestInfoFetcher = Current });
Report("TrackSendEvent", Logs(() => tracker.TrackSendEvent("Publish", "Broker", uri, new { Id = 1 })));
Report("TrackConsumeEvent", Logs(() => tracker.TrackConsumeEvent("Consume", "App", uri, new { Id = 1 })));
Report("TrackMessageRequest + TrackMessageResponse", Logs(() => tracker.TrackMessageResponse("Publish", "Broker", uri, tracker.TrackMessageRequest("Publish", "Broker", uri, new { Id = 1 }))));
Report("TrackSendMessage", Logs(() => tracker.TrackSendMessage("Publish", "Broker", uri, new { Id = 1 })));

Console.WriteLine("-- MessageTracker, with an HTTP context");
var accessor = new HttpContextAccessor();
var correlated = new MessageTracker(new MessageTrackerOptions { ServiceName = "Broker", CallerName = "App", UseHttpContextCorrelation = true, CurrentTestInfoFetcher = Current }, accessor);
HttpContext Inbound(string test, string traceId)
{
    var context = new DefaultHttpContext();
    context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "r3-trace-id";
    context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = test;
    context.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = traceId;
    return context;
}

Report("headers carry a trace id", Logs(() =>
{
    accessor.HttpContext = Inbound(currentTest, Guid.NewGuid().ToString());
    correlated.TrackSendEvent("Publish", "Broker", uri, new { Id = 1 });
    accessor.HttpContext = null;
}));
Report("the trace id header is not a Guid (MessageTracker.cs:378)", Logs(() =>
{
    accessor.HttpContext = Inbound(currentTest, "not-a-guid");
    correlated.TrackSendEvent("Publish", "Broker", uri, new { Id = 1 });
    accessor.HttpContext = null;
}));
Report("request inside the HTTP context, response outside it", Logs(() =>
{
    accessor.HttpContext = Inbound(currentTest, Guid.NewGuid().ToString());
    var id = correlated.TrackMessageRequest("Publish", "Broker", uri, new { Id = 1 });
    accessor.HttpContext = null;
    correlated.TrackMessageResponse("Publish", "Broker", uri, id);
}));

Console.WriteLine("-- NDJSON: requestResponseId pairs, and traceId defaults to it (wiki Ingesting-External-Captures.md:28-29)");
InteractionRecord Line(string type, string? traceId) => InteractionRecord.FromJson($$"""
    {"type":"{{type}}","method":"Publish","uri":"event://cake_events","serviceName":"Broker","callerName":"App","requestResponseId":"call-1","testId":"t1","timestamp":"2026-11-02T10:00:00+00:00"{{(traceId is null ? "" : $",\"traceId\":\"{traceId}\"")}}}
    """);
Report("traceId on both halves", [.. Line("Request", "trace-1").ToLogs(), .. Line("Response", "trace-1").ToLogs()]);
Report("traceId on neither", [.. Line("Request", null).ToLogs(), .. Line("Response", null).ToLogs()]);
Report("traceId on the request alone", [.. Line("Request", "trace-1").ToLogs(), .. Line("Response", null).ToLogs()]);

Console.WriteLine("-- Calls under one Activity: the span id is the Activity's (OtlpSpanMapper.SpanIdFor)");
using (var source = new ActivitySource("r3-probe"))
using (var listener = new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded })
{
    ActivitySource.AddActivityListener(listener);
    using var outer = source.StartActivity("a test's span", ActivityKind.Internal);
    var logs = Logs(() =>
    {
        tracker.TrackSendEvent("Publish", "Broker", uri, new { Id = 1 });
        tracker.TrackSendEvent("Publish", "Broker", uri, new { Id = 2 });
        tracker.TrackSendEvent("Publish", "Broker", uri, new { Id = 3 });
    });
    var batch = OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow);
    Console.WriteLine($"three TrackSendEvent\t{batch.Spans.Count} span(s), {batch.Spans.Select(s => (s.TraceId, s.SpanId, s.Kind)).Distinct().Count()} distinct (trace, span, kind), {batch.Spans.Select(s => (s.TraceId, s.SpanId)).Distinct().Count()} distinct (trace, span)");
}

Console.WriteLine("-- A Kafka produce and the consume of that message, under one test, through the shipped path");
var kafkaOptions = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "App", AutoCorrelateOnConsume = false, CurrentTestInfoFetcher = Current };
var kafka = new KafkaTracker(kafkaOptions);
var kafkaLogs = Logs(() =>
{
    var produce = kafka.TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" }, KafkaOperation.Produce);
    var sent = produce.Message;
    produce.Sent();
    var consumer = new TrackingKafkaConsumer<string, string>(ScriptedConsumer.Returning(new ConsumeResult<string, string>
    {
        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
        Message = sent,
    }), kafka, kafkaOptions);
    consumer.Consume(TimeSpan.Zero);
});
foreach (var span in OtlpSpanMapper.MapAll(kafkaLogs, otlp, DateTimeOffset.UtcNow).Spans)
    Console.WriteLine($"{span.Name}\tkind {span.Kind}\tservice.name {span.ResourceServiceName}\tpeer.service {span.Attribute("peer.service")}\ttrace {span.TraceId}\tspan {span.SpanId}");

class ScriptedConsumer : DispatchProxy
{
    private object? _next;

    public static IConsumer<string, string> Returning(ConsumeResult<string, string> result)
    {
        var proxy = DispatchProxy.Create<IConsumer<string, string>, ScriptedConsumer>();
        ((ScriptedConsumer)(object)proxy)._next = result;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == "Consume" ? _next : targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
}
