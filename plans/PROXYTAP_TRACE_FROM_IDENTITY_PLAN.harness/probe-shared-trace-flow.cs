#:package Kronikol.Extensions.ProxyTap@4.18.1
using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

// Two calls of ONE test through a tap that overlap in time. Each call's downstream service span is a child of the span
// id the tap forwarded for that call. Shape "r1": both calls share the test's trace (TraceIdFromIdentity). Shape
// "today": each call has its own random trace.
var src = new ActivitySource("svc");
using var listener = new ActivityListener { ShouldListenTo = s => s.Name == "svc", Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData };
ActivitySource.AddActivityListener(listener);
var t0 = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

foreach (var shape in new[] { "r1", "today" })
{
    var testId = "4bf92f3577b34da6a3ce929d0e0e4736";
    var s1 = ActivitySpanId.CreateRandom(); var s2 = ActivitySpanId.CreateRandom();
    var tr1 = shape == "r1" ? ActivityTraceId.CreateFromString(testId) : ActivityTraceId.CreateRandom();
    var tr2 = shape == "r1" ? tr1 : ActivityTraceId.CreateRandom();
    var logs = new List<RequestResponseLog>();
    Guid Call(ActivityTraceId tr, ActivitySpanId sp, int startMs, int endMs)
    {
        var id = Guid.NewGuid();
        foreach (var (type, at) in new[] { (RequestResponseType.Request, startMs), (RequestResponseType.Response, endMs) })
            logs.Add(new RequestResponseLog("A scenario", testId, HttpMethod.Get, null, new Uri("http://api/x"), [], "api", "app", type, Guid.NewGuid(), id, false,
                type == RequestResponseType.Response ? HttpStatusCode.OK : null) { Timestamp = t0.AddMilliseconds(at), ActivityTraceId = tr.ToString(), ActivitySpanId = sp.ToString() });
        return id;
    }
    var call1 = Call(tr1, s1, 0, 400);
    var call2 = Call(tr2, s2, 100, 500);
    Activity Span(string name, ActivityTraceId tr, ActivitySpanId parent, int startMs)
    {
        var a = src.StartActivity(name, ActivityKind.Server, new ActivityContext(tr, parent, ActivityTraceFlags.Recorded, isRemote: true), startTime: t0.AddMilliseconds(startMs))!;
        a.SetEndTime(t0.AddMilliseconds(startMs + 50).UtcDateTime); a.Stop(); return a;
    }
    var spans = new[] { Span("api span of call 1", tr1, s1, 150), Span("api span of call 2", tr2, s2, 200) };
    var segments = InternalFlowSegmentBuilder.BuildSegments(logs.ToArray(), spans);
    Console.WriteLine($"shape {shape}:");
    foreach (var (label, id) in new[] { ("call 1", call1), ("call 2", call2) })
        Console.WriteLine($"  {label} popup holds: {(segments.TryGetValue($"iflow-{id}", out var seg) ? string.Join(", ", seg.Spans.Select(s => s.DisplayName)) : "(no segment)")}");
}
