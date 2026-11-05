// What logging a record costs: no step open, a step open in this flow, and a step open with this flow outside it.
// Run with -p:KronikolVersion=... through perf.csproj, once per version.
using System.Diagnostics;
using Kronikol.Tracking;

const int Records = 400_000;
const int Rounds = 5;

static RequestResponseLog Record(string testId) => new(
    testId, testId, "", "", new Uri("http://override.com"), [], "", "",
    RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

static RequestResponseLog Part(string testId, RequestResponseType type, Guid trace, Guid id) => new(
    testId, testId, "", "", new Uri("http://override.com"), [], "", "", type, trace, id, false);

// Per record, where each request is answered: the shape a run has.
static double MeasureAnswered(string testId)
{
    var best = double.MaxValue;
    for (var round = 0; round < Rounds; round++)
    {
        RequestResponseLogger.Clear();
        GC.Collect();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Records / 2; i++)
        {
            var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
            RequestResponseLogger.Log(Part(testId, RequestResponseType.Request, trace, id));
            RequestResponseLogger.Log(Part(testId, RequestResponseType.Response, trace, id));
        }
        best = Math.Min(best, clock.Elapsed.TotalNanoseconds / Records);
    }
    RequestResponseLogger.Clear();
    return best;
}

static double Measure(string testId)
{
    var best = double.MaxValue;
    for (var round = 0; round < Rounds; round++)
    {
        RequestResponseLogger.Clear();
        GC.Collect();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Records; i++)
            RequestResponseLogger.Log(Record(testId));
        best = Math.Min(best, clock.Elapsed.TotalNanoseconds / Records);
    }
    RequestResponseLogger.Clear();
    return best;
}

Console.WriteLine($"running on {typeof(StepCollector).Assembly.GetName().Version}");
Console.WriteLine($"no step in this test           {Measure("no-steps"),8:0} ns per record");

StepCollector.StartStep("in-flow", "Given", "a step", null, null);
StepCollector.StartStep("in-flow", null, "a sub-step", null, null);
Console.WriteLine($"step open, this flow inside it {Measure("in-flow"),8:0} ns per record");
Console.WriteLine($"the same, each request answered {MeasureAnswered("in-flow"),7:0} ns per record");
StepCollector.CompleteStep("in-flow", true);
StepCollector.CompleteStep("in-flow", true);

StepCollector.StartStep("other-flow", "Given", "a step", null, null);
StepCollector.StartStep("other-flow", null, "a sub-step", null, null);
double noFlow = 0;
using (ExecutionContext.SuppressFlow())
{
    var thread = new Thread(() => noFlow = Measure("other-flow"));
    thread.Start();
    thread.Join();
}
Console.WriteLine($"step open, flow not inside it  {noFlow,8:0} ns per record");
