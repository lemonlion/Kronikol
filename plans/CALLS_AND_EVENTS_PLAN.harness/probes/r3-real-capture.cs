// A real capture through the way back: spans real instrumentations wrote, sent by OpenTelemetry's own .NET OTLP exporter (protobuf)
// to a live OtlpTap with Messaging captured. The repository holds no such capture: its one real span file
// (tests/Kronikol.Tests/TestData/Ingest/otel-jest-v2.spans.jsonl) has no messaging span, and OtlpGoldens is written by hand.
//
// - MassTransit over its in-memory transport: its own ActivitySource, no broker needed.
// - Confluent.Kafka under OpenTelemetry's contrib instrumentation against a real broker, when R3_KAFKA names one: a send, a poll and
//   a process, the three kinds at once.
//
// Prints what each span was as written, then what the tap made of it, and what kind it would leave as with IncludeSpanSourced.
//
// R3_SAVE_PAYLOADS names a file to write each export's protobuf to, one base64 line a POST: results/r3-real-capture.payloads.txt, which
// r3-fingerprint.cs replays on 5.27.13 and on R3, so both read the very bytes the real instrumentations wrote.
//
// Usage, from the harness directory, with the broker of probes/r3-real-capture-kafka.sh up:
//   R3_KAFKA=127.0.0.1:39092 R3_SAVE_PAYLOADS=results/r3-real-capture.payloads.txt probes/on-prototype.sh <worktree> probes/r3-real-capture.cs <scratch> > results/r3-real-capture.txt
// (The result file has its topic name, a random id, replaced by r3-real-topic.)
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:package MassTransit@8.5.12
#:package Confluent.Kafka@2.16.0
#:package OpenTelemetry@1.20.1
#:package OpenTelemetry.Exporter.OpenTelemetryProtocol@1.20.1
#:package OpenTelemetry.Instrumentation.ConfluentKafka@0.3.0-alpha.4
#:property PublishAot=false

using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Kronikol.Extensions.Otlp;
using Kronikol.Tracking;
using MassTransit;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.ConfluentKafka;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var kafka = Environment.GetEnvironmentVariable("R3_KAFKA");

var sink = new ListSink();
var options = new OtlpTapOptions { ListenPort = 0, Sink = sink };
options.CaptureKinds.Add(OtlpCaptureKinds.Messaging);
var tap = new OtlpTap(options);
await tap.StartAsync();

var written = new ConcurrentQueue<Activity>();
var topic = "r3-real-" + Guid.NewGuid().ToString("N");

var builder = Sdk.CreateTracerProviderBuilder()
    .ConfigureResource(r => r.AddService("breakfast"))
    .AddSource("MassTransit", "OpenTelemetry.Instrumentation.ConfluentKafka")
    .AddProcessor(new Recorder(written))
    .AddOtlpExporter(o =>
    {
        o.Endpoint = tap.TracesEndpoint;
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
        o.ExportProcessorType = ExportProcessorType.Simple;
        if (Environment.GetEnvironmentVariable("R3_SAVE_PAYLOADS") is { Length: > 0 } saved)
            o.HttpClientFactory = () => new HttpClient(new PayloadRecorder(Path.GetFullPath(saved)) { InnerHandler = new HttpClientHandler() });
    });
using var provider = builder.Build();

// MassTransit, in memory.
var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var bus = Bus.Factory.CreateUsingInMemory(cfg => cfg.ReceiveEndpoint("orders", e => e.Handler<Probe.OrderPlaced>(_ =>
{
    consumed.TrySetResult();
    return Task.CompletedTask;
})));
await bus.StartAsync();
await bus.Publish(new Probe.OrderPlaced("1"));
await consumed.Task.WaitAsync(TimeSpan.FromSeconds(30));
await Task.Delay(500);
await bus.StopAsync();

// Kafka: a send, a poll, then a poll around a handler, which the instrumentation calls process.
if (kafka is not null)
{
    using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka })
        .AsInstrumentedProducerBuilder(new ConfluentKafkaInstrumentedProducerBuilderOptions { EnableTraces = true }).Build();
    using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
    {
        BootstrapServers = kafka, GroupId = "r3-real", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = true,
    }).AsInstrumentedConsumerBuilder(new ConfluentKafkaInstrumentedConsumerBuilderOptions { EnableTraces = true }).Build();
    await producer.ProduceAsync(topic, new Message<string, string> { Key = "k", Value = "first" });
    await producer.ProduceAsync(topic, new Message<string, string> { Key = "k", Value = "second" });
    producer.Flush(TimeSpan.FromSeconds(10));
    consumer.Subscribe(topic);

    var first = consumer.Consume(TimeSpan.FromSeconds(30));
    Console.WriteLine($"# consumed {first?.Message?.Value ?? "nothing"} by Consume");
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await consumer.ConsumeAndProcessMessageAsync((result, _, _) =>
    {
        Console.WriteLine($"# handled {result.Message?.Value ?? "nothing"} by ConsumeAndProcessMessageAsync");
        return ValueTask.CompletedTask;
    }, cancel.Token);
    consumer.Close();
}

provider.ForceFlush();
await tap.DisposeAsync();

Console.WriteLine("== what the instrumentations wrote");
Console.WriteLine("source\tname\tkind\tmessaging.system\tmessaging.operation\tmessaging.operation.name\tmessaging.operation.type\tmessaging.destination.name\tpeer.service\tserver.address");
foreach (var activity in written)
    Console.WriteLine(string.Join('\t', activity.Source.Name, activity.DisplayName, activity.Kind,
        Tag(activity, "messaging.system"), Tag(activity, "messaging.operation"), Tag(activity, "messaging.operation.name"), Tag(activity, "messaging.operation.type"),
        Tag(activity, "messaging.destination.name"), Tag(activity, "peer.service"), Tag(activity, "server.address")));

Console.WriteLine($"== what the tap made of them: {tap.SpansReceived} received, {tap.SpansMapped} mapped, {tap.SpansIgnored} ignored");
Console.WriteLine("metaType\tlabel\turi\tcaller\tservice\tcategory\tkind out (IncludeSpanSourced)\tservice.name out\tpeer.service out\tmessaging.system out\tmessaging.operation.type out");
var again = new OtlpExportOptions { Endpoint = tap.TracesEndpoint, IncludeSpanSourced = true };
var logs = sink.Snapshot();
foreach (var request in logs.Where(l => l.Type == RequestResponseType.Request))
{
    var response = logs.First(l => l.Type == RequestResponseType.Response && l.RequestResponseId == request.RequestResponseId);
    var span = OtlpSpanMapper.Map(request, response, again, DateTimeOffset.UtcNow);
    Console.WriteLine(string.Join('\t', request.MetaType, request.Method.Value, request.Uri, request.CallerName, request.ServiceName, request.DependencyCategory, span.Kind,
        span.ResourceServiceName, span.Attribute("peer.service"), span.Attribute("messaging.system") ?? "-", span.Attribute("messaging.operation.type") ?? "-"));
}

return 0;

static string Tag(Activity activity, string key) => activity.GetTagItem(key)?.ToString() ?? "-";

namespace Probe
{
    public record OrderPlaced(string Id);
}

sealed class PayloadRecorder(string path) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var answer = new ByteArrayContent(body);
        foreach (var header in request.Content.Headers)
            answer.Headers.TryAddWithoutValidation(header.Key, header.Value);
        request.Content = answer;
        lock (typeof(PayloadRecorder))
            File.AppendAllText(path, Convert.ToBase64String(body) + "\n");
        return await base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();
}

sealed class Recorder(ConcurrentQueue<Activity> seen) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data) => seen.Enqueue(data);
}

sealed class ListSink : IRequestResponseSink
{
    private readonly ConcurrentQueue<RequestResponseLog> _logs = new();
    public void Log(RequestResponseLog log) => _logs.Enqueue(log);
    public RequestResponseLog[] Snapshot() => _logs.ToArray();
}
