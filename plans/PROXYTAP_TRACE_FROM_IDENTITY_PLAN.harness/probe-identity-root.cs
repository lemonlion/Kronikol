using System.Diagnostics;
var src = new ActivitySource("probe");
var samples = new List<string>();
using var l = new ActivityListener
{
    ShouldListenTo = s => s.Name == "probe",
    Sample = (ref ActivityCreationOptions<ActivityContext> o) =>
    {
        samples.Add($"sampler saw parent trace={o.Parent.TraceId} span={o.Parent.SpanId} flags={o.Parent.TraceFlags} remote={o.Parent.IsRemote} traceIdInOptions={o.TraceId}");
        return ActivitySamplingResult.AllDataAndRecorded;
    },
};
ActivitySource.AddActivityListener(l);
var trace = ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736");
void Show(string label, Activity? a) => Console.WriteLine($"{label}: id={a?.Id} parentSpan={a?.ParentSpanId} hasRemoteParent={a?.HasRemoteParent} parentId={a?.ParentId ?? "(null)"}");
using (var a = src.StartActivity("A default span", ActivityKind.Server, new ActivityContext(trace, default, ActivityTraceFlags.Recorded, isRemote: true)))
{
    Show("A (parent span default, remote)", a);
    using var c = src.StartActivity("child", ActivityKind.Client);
    Show("  A.child", c);
}
using (var b = src.StartActivity("B random span", ActivityKind.Server, new ActivityContext(trace, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true)))
    Show("B (random parent span, remote)", b);
using (var d = src.StartActivity("D default span not remote", ActivityKind.Server, new ActivityContext(trace, default, ActivityTraceFlags.Recorded)))
    Show("D (parent span default, local)", d);
foreach (var s in samples) Console.WriteLine(s);
try { ActivityTraceId.CreateFromString("4BF92F3577B34DA6A3CE929D0E0E4736"); Console.WriteLine("upper ok"); } catch (Exception e) { Console.WriteLine("upper: " + e.GetType().Name); }
try { ActivityTraceId.CreateFromString("00000000000000000000000000000000"); Console.WriteLine("zeros ok"); } catch (Exception e) { Console.WriteLine("zeros: " + e.GetType().Name); }
Console.WriteLine(Environment.Version);
