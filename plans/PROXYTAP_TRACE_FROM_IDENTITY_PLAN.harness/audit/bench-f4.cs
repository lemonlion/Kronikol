#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// D1: how long BuildSegments takes when one test's calls share one trace under an ambient span (the in-process
// shape: TestTrackingMessageHandler records Activity.Current's span for every call the test makes) and each call's
// service makes a downstream call under its own request span. Args: calls per test, spans per call.
using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

var n = args.Length > 0 ? int.Parse(args[0]) : 300;
var k = args.Length > 1 ? int.Parse(args[1]) : 10;
var source = new ActivitySource("bench");
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == "bench",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);

var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
var trace = ActivityTraceId.CreateRandom().ToString();
var spans = new List<Activity>();
Activity Span(string name, double ms, string? parent)
{
    Activity.Current = null;
    var ctx = new ActivityContext(ActivityTraceId.CreateFromString(trace),
        parent is null ? default : ActivitySpanId.CreateFromString(parent), ActivityTraceFlags.Recorded);
    var s = source.StartActivity(name, ActivityKind.Internal, ctx)!;
    s.SetStartTime(t0.UtcDateTime.AddMilliseconds(ms));
    s.SetEndTime(t0.UtcDateTime.AddMilliseconds(ms + 1));
    spans.Add(s);
    return s;
}

var ambient = Span("test", 0, null);
var logs = new List<RequestResponseLog>();
void Call(double from, double to, string service, string caller, string spanId)
{
    var id = Guid.NewGuid();
    var kt = Guid.NewGuid();
    RequestResponseLog Log(RequestResponseType type, double ms) =>
        new("Test", "t1", HttpMethod.Get, null, new Uri("http://sut/x"), [], service, caller, type, kt, id, false)
        { Timestamp = t0.AddMilliseconds(ms), ActivityTraceId = trace, ActivitySpanId = spanId };
    logs.Add(Log(RequestResponseType.Request, from));
    logs.Add(Log(RequestResponseType.Response, to));
}

for (var i = 0; i < n; i++)
{
    var at = 10.0 * i + 1;
    Call(at, at + 8, "Api", "Test", ambient.SpanId.ToString());          // the test's call, under the ambient span
    var server = Span("api-server", at + 0.5, ambient.SpanId.ToString());
    Call(at + 1, at + 6, "Db", "Api", server.SpanId.ToString());        // the service's downstream call
    for (var j = 0; j < k; j++)
        Span("work", at + 1 + j * 0.4, server.SpanId.ToString());
}

var arr = logs.ToArray();
var acts = spans.ToArray();
InternalFlowSegmentBuilder.BuildSegments(arr, acts); // warm
var sw = Stopwatch.StartNew();
var segments = InternalFlowSegmentBuilder.BuildSegments(arr, acts);
sw.Stop();
Console.WriteLine($"calls={arr.Length / 2} spans={acts.Length} segments={segments.Count} ms={sw.ElapsedMilliseconds} spansInSegments={segments.Values.Sum(s => s.Spans.Count())}");
