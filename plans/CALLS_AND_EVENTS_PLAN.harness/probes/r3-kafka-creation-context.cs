// OTel's send row: "PRODUCER if the context of the "Send" span is used as creation context, otherwise CLIENT". Is it, for the one tracker
// that injects one? Each row is a Kafka produce through the shipped path: the traceparent the message left with, beside the ids of the span
// exported for it. With R3_KAFKA naming a broker, also a real produce through TrackingKafkaProducer, read back by a plain consumer.
//
// Usage, from the harness directory (R3 moves nothing on this path, so the published packages give the same ids):
//   R3_KAFKA=127.0.0.1:39092 probes/on-prototype.sh <worktree> probes/r3-kafka-creation-context.cs <scratch> > results/r3-kafka-creation-context.txt
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:package Kronikol.Extensions.Kafka@5.27.13
#:property PublishAot=false

using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;

var otlp = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };
var currentTest = "";
(string, string) Current() => ("r3-kafka-context", currentTest);

static string? TraceParent(Headers? headers)
{
    if (headers is null)
        return null;
    string? last = null;
    foreach (var header in headers)
        if (header.Key == "traceparent")
            last = Encoding.UTF8.GetString(header.GetValueBytes());
    return last;
}

Console.WriteLine("case\ttraceparent on the message\tambient span\texported kind\texported trace\texported span\tinjected span is the exported span");

void Report(string name, string? traceparent, string? ambient)
{
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == currentTest).ToArray();
    var span = OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow).Spans.Single();
    var injected = traceparent?.Split('-');
    var same = injected is null ? "nothing injected" : injected[2] == span.SpanId && injected[1] == span.TraceId ? "yes, trace and span" : "NO";
    Console.WriteLine(string.Join('\t', name, traceparent ?? "-", ambient ?? "-", span.Kind, span.TraceId, span.SpanId, same));
}

void Case(string name, bool propagate, bool ambient, string? callersOwn)
{
    currentTest = Guid.NewGuid().ToString("N");
    var options = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "App", PropagateTraceContext = propagate, CurrentTestInfoFetcher = Current };
    var tracker = new KafkaTracker(options);
    using var activity = ambient ? new Activity("ambient").Start() : null;
    var message = new Message<string, string> { Key = "k", Value = "v" };
    if (callersOwn is not null)
        message.Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(callersOwn) } };

    var produce = tracker.TrackProduce("orders", message, KafkaOperation.Produce);
    produce.Sent();

    Report(name, TraceParent(produce.Message.Headers), activity?.SpanId.ToString());
}

Case("default, an Activity current", propagate: true, ambient: true, callersOwn: null);
Case("default, no Activity", propagate: true, ambient: false, callersOwn: null);
Case("PropagateTraceContext off, an Activity current", propagate: false, ambient: true, callersOwn: null);
Case("PropagateTraceContext off, no Activity", propagate: false, ambient: false, callersOwn: null);
Case("the caller's own traceparent, no Activity", propagate: true, ambient: false, callersOwn: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
Case("the caller's own traceparent, an Activity current", propagate: true, ambient: true, callersOwn: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

// KafkaTracker.LogProduce: a producer the wrappers do not hold. Nothing is sent by it, so nothing is injected.
{
    currentTest = Guid.NewGuid().ToString("N");
    var options = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "App", CurrentTestInfoFetcher = Current };
    var tracker = new KafkaTracker(options);
    using var activity = new Activity("ambient").Start();
    tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.Produce, "orders"), "v");
    Report("KafkaTracker.LogProduce by hand", null, activity.SpanId.ToString());
}

var broker = Environment.GetEnvironmentVariable("R3_KAFKA");
if (broker is not null)
{
    currentTest = Guid.NewGuid().ToString("N");
    var topic = "r3-context-" + Guid.NewGuid().ToString("N");
    var options = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "App", CurrentTestInfoFetcher = Current };
    var tracker = new KafkaTracker(options);
    using var inner = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = broker }).Build();
    using var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
    using var activity = new Activity("ambient").Start();
    await producer.ProduceAsync(topic, new Message<string, string> { Key = "k", Value = "v" });

    using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
    {
        BootstrapServers = broker, GroupId = "r3-context", AutoOffsetReset = AutoOffsetReset.Earliest,
    }).Build();
    consumer.Subscribe(topic);
    var read = consumer.Consume(TimeSpan.FromSeconds(30));
    consumer.Close();
    Report("a real broker: TrackingKafkaProducer, read back by a plain consumer", TraceParent(read?.Message?.Headers), activity.SpanId.ToString());
}
