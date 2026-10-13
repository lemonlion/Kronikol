// What a listener that throws in ActivityStarted or ActivityStopped leaves behind: Activity.Current, the span's state.
using System.Diagnostics;
var source = new ActivitySource("probe");
foreach (var where in new[] { "start", "stop" })
{
    using var listener = new ActivityListener
    {
        ShouldListenTo = s => s.Name == "probe",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStarted = a => { if (where == "start") throw new InvalidOperationException("start threw"); },
        ActivityStopped = a => { if (where == "stop") throw new InvalidOperationException("stop threw"); },
    };
    ActivitySource.AddActivityListener(listener);
    var outer = new Activity("outer").Start();
    Activity? span = null;
    try { span = source.StartActivity("span"); Console.WriteLine($"{where}: started, Current={Activity.Current?.OperationName}"); }
    catch (Exception ex) { Console.WriteLine($"{where}: StartActivity threw {ex.Message}; Current={Activity.Current?.OperationName}, Current.Parent={Activity.Current?.Parent?.OperationName}"); }
    if (span is not null)
    {
        try { span.Dispose(); Console.WriteLine($"{where}: disposed, Current={Activity.Current?.OperationName}"); }
        catch (Exception ex) { Console.WriteLine($"{where}: Dispose threw {ex.Message}; Current={Activity.Current?.OperationName}, span stopped={span.IsStopped}, duration set={span.Duration > TimeSpan.Zero}"); }
    }
    Activity.Current = null; outer.Stop();
}
