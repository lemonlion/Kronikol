using System.Diagnostics;
using System.Reflection;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

// Probe for the adversarial review of KAFKA_INTERCEPTION_AND_TRACE_PLAN.md (sections 4.2-4.6), on 4.14.6's DLLs.
static void L(string s) => Console.WriteLine(s);

// ---------------------------------------------------------------- P0: the FailedSend convention on Kafka's exception
var pe = new ProduceException<string, string>(new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<string, string>());
L("P0 FailedSend.Status(ProduceException<string,string>) = " + FailedSend.Status(pe));
L("P0 FailedSend.Describe(...)                         = " + FailedSend.Describe(pe));
L("P0 InteractionStatus.IsError(null, \"!Local_MsgTimedOut\") = " + Kronikol.Reports.InteractionStatus.IsError(null, "!Local_MsgTimedOut"));

// ---------------------------------------------------------------- P1 (H5): the background identity travels on a message
RequestResponseLogger.Clear();
RequestResponseLogger.CaptureBackground = true;
TestIdentityScope.ClearGlobalFallback();
var pOpts = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "Host A", PropagateTestIdentity = true };
var pTracker = new KafkaTracker(pOpts);
var fakeP = DispatchProxy.Create<IProducer<string, string>, FakeProducer>();
var producer = new TrackingKafkaProducer<string, string>(fakeP, pTracker, pOpts);
var msg = new Message<string, string> { Key = "k1", Value = "{\"a\":1}" };
await Task.Run(async () => await producer.ProduceAsync("orders", msg));
L("P1 headers the producer stamped while nothing resolved: " + string.Join(", ", msg.Headers.Select(h => h.Key + "=" + Encoding.UTF8.GetString(h.GetValueBytes()))));
L("P1 producer-side entries: " + string.Join(" | ", RequestResponseLogger.RequestAndResponseLogs.Select(l => $"{l.Type} test={l.TestId} source={l.AttributionSource}")));
RequestResponseLogger.Clear();

var cOpts = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "Host B", PropagateTestIdentity = true };
var cTracker = new KafkaTracker(cOpts);
var fakeC = DispatchProxy.Create<IConsumer<string, string>, FakeConsumer>();
((FakeConsumer)(object)fakeC).Next = new ConsumeResult<string, string> { Topic = "orders", Partition = 0, Offset = 5, Message = msg };
var consumer = new TrackingKafkaConsumer<string, string>(fakeC, cTracker, cOpts);
await Task.Run(() =>
{
    var before = TestInfoResolver.ResolveWithSource(null, (Func<(string Name, string Id)>?)null);
    L($"P1 consumer flow before Consume: {before?.Name}/{before?.Id} source={before?.Source} attributed={before?.IsAttributed}");
    consumer.Consume(TimeSpan.Zero);
    var after = TestInfoResolver.ResolveWithSource(null, (Func<(string Name, string Id)>?)null);
    L($"P1 consumer flow after Consume:  {after?.Name}/{after?.Id} source={after?.Source} attributed={after?.IsAttributed}");
    L("P1 correlation stored for the key: " + TestCorrelationStore.Lookup(CorrelationKeys.Kafka("Kafka", "k1")));
});
L("P1 consume entries: " + string.Join(" | ", RequestResponseLogger.RequestAndResponseLogs.Select(l => $"{l.Type} test={l.TestId} source={l.AttributionSource}")));
RequestResponseLogger.Clear();
RequestResponseLogger.CaptureBackground = false;
TestCorrelationStore.Clear();

// P1b: a consumer whose fetcher answers another test (a host built inside test Y, not detached) records Y, whatever the message says
var yOpts = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "Host B", PropagateTestIdentity = true, CurrentTestInfoFetcher = () => ("Test Y", "y-id") };
var yConsumer = new TrackingKafkaConsumer<string, string>(fakeC, new KafkaTracker(yOpts), yOpts);
var xMsg = new Message<string, string> { Key = "k2", Value = "v", Headers = new Headers { { "kronikol-test-name", Encoding.UTF8.GetBytes("Test X") }, { "kronikol-test-id", Encoding.UTF8.GetBytes("x-id") } } };
((FakeConsumer)(object)fakeC).Next = new ConsumeResult<string, string> { Topic = "orders", Partition = 0, Offset = 6, Message = xMsg };
await Task.Run(() => yConsumer.Consume(TimeSpan.Zero));
L("P1b consume of X's message under a fetcher that answers Y: " + string.Join(" | ", RequestResponseLogger.RequestAndResponseLogs.Select(l => $"{l.Type} test={l.TestId} source={l.AttributionSource}")));
RequestResponseLogger.Clear();

// ---------------------------------------------------------------- P2 (H7): what a consume's W3C ids do to the whole-test flow
var spans = new List<Activity>();
using (var listener = new ActivityListener
{
    ShouldListenTo = _ => true,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a => { lock (spans) spans.Add(a); }
})
{
    ActivitySource.AddActivityListener(listener);
    var asp = new ActivitySource("Microsoft.AspNetCore");
    var app = new ActivitySource("App");
    Activity.Current = null;

    // The test's HTTP call: no ambient Activity, so the handler records and injects random ids (T, S0).
    var traceT = ActivityTraceId.CreateRandom();
    var s0 = ActivitySpanId.CreateRandom();
    var tHttpReq = DateTimeOffset.UtcNow;
    Thread.Sleep(2);
    var server = asp.StartActivity("POST /orders", ActivityKind.Server, new ActivityContext(traceT, s0, ActivityTraceFlags.None, null, true))!;
    Thread.Sleep(2);
    var db = app.StartActivity("db insert")!; Thread.Sleep(1); db.Stop();
    var tProduce = DateTimeOffset.UtcNow;  // R1: produce records Activity.Current = the server span; injects traceparent (T, server)
    Thread.Sleep(2);
    server.Stop();
    var tHttpResp = DateTimeOffset.UtcNow;
    Activity.Current = null;
    Thread.Sleep(5);

    // Consumer side, after the POST answered. Kronikol's consume entry is logged first (one instant), then the handler runs.
    var tConsume = DateTimeOffset.UtcNow;
    Thread.Sleep(2);
    // (a) a consumer that PARENTS its processing span from traceparent (Confluent.Kafka.Extensions.Diagnostics)
    var parented = app.StartActivity("process (parented, vhatsura-style)", ActivityKind.Consumer, new ActivityContext(traceT, server.SpanId, ActivityTraceFlags.None, null, true))!;
    var parentedChild = app.StartActivity("handler db read (parented)")!; Thread.Sleep(1); parentedChild.Stop();
    parented.Stop();
    Activity.Current = null;
    // (b) a consumer that LINKS (OpenTelemetry contrib ConsumeAndProcessMessageAsync): a new root trace
    var linked = app.StartActivity("process (linked, OTel-contrib-style)", ActivityKind.Consumer, default(ActivityContext),
        links: [new ActivityLink(new ActivityContext(traceT, server.SpanId, ActivityTraceFlags.None, null, true))])!;
    var linkedChild = app.StartActivity("handler db read (linked)")!; Thread.Sleep(1); linkedChild.Stop();
    linked.Stop();
    Activity.Current = null;
    var tAfterHandler = DateTimeOffset.UtcNow;

    var spanArr = spans.ToArray();
    RequestResponseLog Rec(string testId, RequestResponseType type, Guid rr, DateTimeOffset at, string? trace, string? span, string caller, string service, string method) =>
        new(testId + " name", testId, method, null, new Uri("kafka:///orders"), [], service, caller, type, Guid.NewGuid(), rr, false)
        { Timestamp = at, ActivityTraceId = trace, ActivitySpanId = span };

    var httpId = Guid.NewGuid();
    var http = new[]
    {
        Rec("x-id", RequestResponseType.Request, httpId, tHttpReq, traceT.ToString(), s0.ToString(), "Test", "Host A", "POST"),
        Rec("x-id", RequestResponseType.Response, httpId, tHttpResp, traceT.ToString(), s0.ToString(), "Test", "Host A", "POST"),
    };
    RequestResponseLog[] Kafka(string consumeTestId, bool ids, DateTimeOffset consumeEnd)
    {
        var pid = Guid.NewGuid(); var cid = Guid.NewGuid();
        string? tr = ids ? traceT.ToString() : null; string? sp = ids ? server.SpanId.ToString() : null;
        return
        [
            Rec("x-id", RequestResponseType.Request, pid, tProduce, tr, sp, "Host A", "Kafka", "Produce"),
            Rec("x-id", RequestResponseType.Response, pid, tProduce, tr, sp, "Host A", "Kafka", "Produce"),
            Rec(consumeTestId, RequestResponseType.Request, cid, tConsume, tr, sp, "Kafka", "Host B", "Consume"),
            Rec(consumeTestId, RequestResponseType.Response, cid, consumeEnd, tr, sp, "Kafka", "Host B", "Consume"),
        ];
    }
    string Whole(RequestResponseLog[] logs, string test) =>
        InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, spanArr).TryGetValue("iflow-test-" + test, out var seg)
            ? $"{seg.Spans.Length} spans [{string.Join("; ", seg.Spans.Select(s => s.DisplayName))}]" : "no segment";
    string Popup(RequestResponseLog[] logs) =>
        InternalFlowSegmentBuilder.BuildSegments(logs, spanArr).TryGetValue("iflow-" + logs.First(l => (string)l.Method.Value == "Consume").RequestResponseId, out var seg)
            ? $"{seg.Spans.Length} spans [{string.Join("; ", seg.Spans.Select(s => s.DisplayName))}]" : "no segment";

    L("P2 spans recorded: " + string.Join("; ", spanArr.Select(s => $"{s.DisplayName} trace={(s.TraceId == traceT ? "T" : "other")}")));
    L("P2 today (no Kafka ids), consume under X:      whole-test X = " + Whole([.. http, .. Kafka("x-id", false, tConsume)], "x-id"));
    L("P2 R1 ids, consume under X:                    whole-test X = " + Whole([.. http, .. Kafka("x-id", true, tConsume)], "x-id"));
    L("P2 R1 ids, consume under Y (fetcher, P1b):      whole-test X = " + Whole([.. http, .. Kafka("y-id", true, tConsume)], "x-id"));
    L("P2 R1 ids, consume under unknown (background): whole-test X = " + Whole([.. http, .. Kafka("unknown", true, tConsume)], "x-id"));
    L("P2 R1 ids, consume under X, one-instant consume popup = " + Popup([.. http, .. Kafka("x-id", true, tConsume)]));
    L("P2 R1 ids + R2 window (response after handler), popup  = " + Popup([.. http, .. Kafka("x-id", true, tAfterHandler)]));
    string PostPopup(RequestResponseLog[] logs) =>
        InternalFlowSegmentBuilder.BuildSegments(logs, spanArr).TryGetValue("iflow-" + httpId, out var seg)
            ? $"{seg.Spans.Length} spans [{string.Join("; ", seg.Spans.Select(s => s.DisplayName))}]" : "no segment";
    L("P2 POST /orders popup, today (no Kafka ids), consume under Y: " + PostPopup([.. http, .. Kafka("y-id", false, tConsume)]));
    L("P2 POST /orders popup, R1 ids, consume under X:              " + PostPopup([.. http, .. Kafka("x-id", true, tConsume)]));
    L("P2 POST /orders popup, R1 ids, consume under Y:              " + PostPopup([.. http, .. Kafka("y-id", true, tConsume)]));
    L($"P2 gap between the producer host's db span and the consume entry: {(tConsume - new DateTimeOffset(db.StartTimeUtc, TimeSpan.Zero)).TotalMilliseconds:F1} ms (builder tolerance 50 ms)");
}

// ---------------------------------------------------------------- P3 (H12): an Activity left current by Consume and stopped at the next one
var r2 = new ActivitySource("Kronikol.Kafka");
var loopSrc = new ActivitySource("App.Loop");
using (var l2 = new ActivityListener
{
    ShouldListenTo = _ => true,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
})
{
    ActivitySource.AddActivityListener(l2);
    Activity? pending = null;
    void ConsumeR2(string name) { pending?.Stop(); pending = r2.StartActivity(name + " process", ActivityKind.Consumer); }

    Activity.Current = null; pending = null;
    ConsumeR2("direct");
    L($"P3a Consume called directly: Activity.Current after = {Activity.Current?.DisplayName ?? "null"}");

    Activity.Current = null; pending = null;
    await Task.Run(() => ConsumeR2("taskrun"));
    L($"P3b Consume called in await Task.Run: Activity.Current after = {Activity.Current?.DisplayName ?? "null"} (the handler's spans are not its children)");

    Activity.Current = null; pending = null;
    var a1 = loopSrc.StartActivity("iteration 1")!;
    ConsumeR2("m1"); var p1 = pending!;
    a1.Dispose();
    var a2 = loopSrc.StartActivity("iteration 2")!;
    ConsumeR2("m2"); var p2 = pending!;
    L($"P3c loop wraps each Consume in its own Activity: m2's parent = {p2.Parent?.DisplayName} (stopped={p2.Parent?.IsStopped}); expected iteration 2; m1 stop restored Current to a stopped activity");
    p2.Stop(); a2.Dispose(); Activity.Current = null;

    Activity? pa = null, pb = null;
    void ConsumeA(string n) { pa?.Stop(); pa = r2.StartActivity("A " + n); }
    void ConsumeB(string n) { pb?.Stop(); pb = r2.StartActivity("B " + n); }
    Activity.Current = null;
    ConsumeA("1"); var paFirst = pa!; ConsumeB("1");
    ConsumeA("2"); ConsumeB("2");
    L($"P3d two consumers in one loop: B 2's parent = {pb!.Parent?.DisplayName} (stopped={pb.Parent?.IsStopped}), same as A 1: {ReferenceEquals(pb.Parent, paFirst)}");
    pa!.Stop(); pb!.Stop(); Activity.Current = null;

    pending = null;
    ConsumeR2("h1");
    var leaked = loopSrc.StartActivity("handler leaves its own activity current")!;
    ConsumeR2("h2");
    L($"P3e handler left an Activity current: after next Consume, the leaked one stopped={leaked.IsStopped}, h2's parent = {pending!.Parent?.DisplayName ?? "null"}");
    pending.Stop(); Activity.Current = null;
}

// ---------------------------------------------------------------- P4 (H4, H11): Confluent's own producer, no broker
var config = new ProducerConfig { BootstrapServers = "127.0.0.1:1", MessageTimeoutMs = 1500, SocketTimeoutMs = 1000 };
using (var p = new ProducerBuilder<string, string>(config).SetValueSerializer(new ThrowingSerializer()).Build())
{
    var m = new Message<string, string> { Key = "k", Value = "v" };
    try { await p.ProduceAsync("t", m); }
    catch (ProduceException<string, string> e) { L($"P4a serializer failure: {e.Error.Code}; ex.DeliveryResult.Message is the caller's object: {ReferenceEquals(e.DeliveryResult.Message, m)}"); }
}
using (var p = new ProducerBuilder<string, string>(config).Build())
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var m = new Message<string, string> { Key = "k", Value = "v" };
    try { await p.ProduceAsync("t", m, cts.Token); L("P4b no exception"); }
    catch (Exception e) { L($"P4b ProduceAsync with a cancelled token threw {e.GetType().Name}"); }
    L($"P4b messages still queued in librdkafka after that cancelled ProduceAsync: {p.Flush(TimeSpan.FromMilliseconds(200))}");

    DeliveryReport<string, string>? report = null;
    var m2 = new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "h", [1] } } };
    p.Produce("t", m2, r => report = r);
    p.Flush(TimeSpan.FromSeconds(5));
    L($"P4c delivery report: error={report?.Error.Code} reason='{report?.Error.Reason}' report.Message is the caller's object: {ReferenceEquals(report?.Message, m2)}");
}

// ---------------------------------------------------------------- P5 (H8): three traceparent parsers
string lower = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
string[] cases =
[
    lower,
    lower.ToUpperInvariant(),
    "ff" + lower[2..],
    "00-0af7651916cd43dd8448eb211c80319c-0000000000000000-01",
    "01" + lower[2..],
    lower + "-extra",
    "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-1",
];
foreach (var c in cases)
{
    var net = ActivityContext.TryParse(c, null, out var ctx);
    var tap = ProxyTapCopy.TryParse(c);
    L($"P5 {c,-62} .NET ActivityContext.TryParse={net,-5} ProxyTap TraceParent.TryParse={(tap is null ? "null" : tap.Value.TraceId[..8] + "…")}");
}

// ---------------------------------------------------------------- P6 (H9): a preset Timestamp survives Log
RequestResponseLogger.Clear();
var preset = DateTimeOffset.UtcNow.AddSeconds(-3);
RequestResponseLogger.Log(new RequestResponseLog("n", "preset-id", "Produce", null, new Uri("kafka:///t"), [], "Kafka", "Host A", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = preset });
L($"P6 preset Timestamp kept by RequestResponseLogger.Log: {RequestResponseLogger.RequestAndResponseLogs.Single(l => l.TestId == "preset-id").Timestamp == preset}");

// ---------------------------------------------------------------- P7 (H4): today's wrapper writes into the caller's message
RequestResponseLogger.Clear();
var who = ("Test 1", "t1");
var reuseOpts = new KafkaTrackingOptions { ServiceName = "Kafka", CallerName = "Host A", CurrentTestInfoFetcher = () => who };
var reuseFake = DispatchProxy.Create<IProducer<string, string>, FakeProducer>();
var reuse = new TrackingKafkaProducer<string, string>(reuseFake, new KafkaTracker(reuseOpts), reuseOpts);
var reused = new Message<string, string> { Key = "k", Value = "v" };
await reuse.ProduceAsync("orders", reused);
who = ("Test 2", "t2");
await reuse.ProduceAsync("orders", reused);
L("P7 the caller's message after two produces: " + string.Join(", ", reused.Headers.Select(h => h.Key + "=" + Encoding.UTF8.GetString(h.GetValueBytes()))));
L("P7 the inner producer was handed the caller's object both times: " + ((FakeProducer)(object)reuseFake).Sent.All(s => ReferenceEquals(s, reused)));

// ---------------------------------------------------------------- P8 (H6): `flow` nesting, rule R4, for test -> A (HTTP) -> produce -> consume in B -> B's HTTP call
// G1 is the POST's Kronikol trace id (test-tracking-trace-id); Kafka entries today take Guid.NewGuid() (KafkaTracker.cs:112, :160).
const string Req = "Request", Resp = "Response";
Nesting.Show("S1 today, the consume recorded after the POST answered", [
    (Req, "Test", "Host A", "post", "G1", "POST /orders"),
    (Req, "Host A", "Kafka", "prod", "G2", "Produce (A)"), (Resp, "Host A", "Kafka", "prod", "G2", ""),
    (Resp, "Test", "Host A", "post", "G1", ""),
    (Req, "Kafka", "Host B", "cons", "G3", "Consume (B)"), (Resp, "Kafka", "Host B", "cons", "G3", ""),
    (Req, "Host B", "Service C", "getc", "G4", "GET C (B, in handler)"), (Resp, "Host B", "Service C", "getc", "G4", ""),
]);
Nesting.Show("S2 today, the consume recorded while the POST still waits (A awaits a reply)", [
    (Req, "Test", "Host A", "post", "G1", "POST /orders"),
    (Req, "Host A", "Kafka", "prod", "G2", "Produce (A)"), (Resp, "Host A", "Kafka", "prod", "G2", ""),
    (Req, "Kafka", "Host B", "cons", "G3", "Consume (B)"), (Resp, "Kafka", "Host B", "cons", "G3", ""),
    (Req, "Host B", "Service C", "getc", "G4", "GET C (B, in handler)"), (Resp, "Host B", "Service C", "getc", "G4", ""),
    (Resp, "Test", "Host A", "post", "G1", ""),
]);
Nesting.Show("S3 = S2 with the Kronikol trace id carried on the message (produce takes G1, consume reads it)", [
    (Req, "Test", "Host A", "post", "G1", "POST /orders"),
    (Req, "Host A", "Kafka", "prod", "G1", "Produce (A)"), (Resp, "Host A", "Kafka", "prod", "G1", ""),
    (Req, "Kafka", "Host B", "cons", "G1", "Consume (B)"), (Resp, "Kafka", "Host B", "cons", "G1", ""),
    (Req, "Host B", "Service C", "getc", "G4", "GET C (B, in handler)"), (Resp, "Host B", "Service C", "getc", "G4", ""),
    (Resp, "Test", "Host A", "post", "G1", ""),
]);
Nesting.Show("S4 = S3 with R2 (the consume answered at the next Consume, after the handler)", [
    (Req, "Test", "Host A", "post", "G1", "POST /orders"),
    (Req, "Host A", "Kafka", "prod", "G1", "Produce (A)"), (Resp, "Host A", "Kafka", "prod", "G1", ""),
    (Req, "Kafka", "Host B", "cons", "G1", "Consume (B)"),
    (Req, "Host B", "Service C", "getc", "G4", "GET C (B, in handler)"), (Resp, "Host B", "Service C", "getc", "G4", ""),
    (Resp, "Kafka", "Host B", "cons", "G1", ""),
    (Resp, "Test", "Host A", "post", "G1", ""),
]);
Nesting.Show("S5 F8 enqueued as two halves: produce request at the call, response at delivery; B consumes an unrelated message in between", [
    (Req, "Host A", "Kafka", "prod", "G2", "Produce (A)"),
    (Req, "Kafka", "Host B", "other", "G9", "Consume (B, other message)"), (Resp, "Kafka", "Host B", "other", "G9", ""),
    (Resp, "Host A", "Kafka", "prod", "G2", ""),
]);

public static class Nesting
{
    // Drives the internal CallNesting.Parents (rule R4) by reflection over InteractionEntry records.
    public static void Show(string title, (string Type, string Caller, string Service, string Rr, string Trace, string Label)[] rows)
    {
        var asm = typeof(RequestResponseLogger).Assembly;
        var entryType = asm.GetType("Kronikol.Query.InteractionEntry", true)!;
        var nesting = asm.GetType("Kronikol.Query.CallNesting", true)!;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
        foreach (var r in rows)
        {
            var e = Activator.CreateInstance(entryType, nonPublic: true)!;
            entryType.GetProperty("Type")!.SetValue(e, r.Type);
            entryType.GetProperty("CallerName")!.SetValue(e, r.Caller);
            entryType.GetProperty("ServiceName")!.SetValue(e, r.Service);
            entryType.GetProperty("RequestResponseId")!.SetValue(e, r.Rr);
            entryType.GetProperty("TraceId")!.SetValue(e, r.Trace);
            list.Add(e);
        }
        var parents = (int?[])nesting.GetMethod("Parents", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [list])!;
        Console.WriteLine("P8 " + title);
        for (var i = 0; i < rows.Length; i++)
            if (rows[i].Type == "Request")
                Console.WriteLine($"     {rows[i].Label,-28} parent: {(parents[i] is { } p ? rows[p].Label : "(top level)")}");
    }
}

public class FakeProducer : DispatchProxy
{
    public List<Message<string, string>> Sent { get; } = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "ProduceAsync":
                var m = (Message<string, string>)args![1]!;
                Sent.Add(m);
                return Task.FromResult(new DeliveryResult<string, string> { Topic = "orders", Partition = 0, Offset = Sent.Count, Message = m });
            case "get_Name": return "fake";
            case "Dispose": return null;
            default: throw new NotImplementedException(method.Name);
        }
    }
}

public class FakeConsumer : DispatchProxy
{
    public ConsumeResult<string, string>? Next { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "Consume" => Next,
        "get_Name" => "fake",
        "Dispose" => null,
        _ => throw new NotImplementedException(method.Name)
    };
}

public class ThrowingSerializer : ISerializer<string>
{
    public byte[] Serialize(string data, SerializationContext context) => throw new InvalidOperationException("boom");
}

// Copied verbatim from src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs:181-193 (4.14.6), returning a tuple.
public static class ProxyTapCopy
{
    public static (string TraceId, string ParentSpanId, byte Flags)? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var parts = value.Trim().Split('-');
        if (parts.Length < 4 || parts[1].Length != 32 || parts[2].Length != 16 || parts[3].Length != 2)
            return null;
        if (!IsHex(parts[1]) || !IsHex(parts[2]) || !IsHex(parts[3]))
            return null;
        if (parts[1].All(c => c == '0'))
            return null;
        return (parts[1].ToLowerInvariant(), parts[2].ToLowerInvariant(), Convert.ToByte(parts[3], 16));
    }
    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);
}
