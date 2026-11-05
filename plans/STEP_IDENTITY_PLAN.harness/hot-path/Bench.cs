// What the stamp costs on the path every tracked call takes. Run with -c Release, on the published package and on the
// prototype (-p:KronikolProto=<worktree>). Prints microseconds per logged pair, the median of the rounds.
using System.Diagnostics;
using Kronikol.Tracking;

const int Rounds = 7;
const int Pairs = 100_000;
var uri = new Uri("http://shop.test/orders");

double Measure(Action<int> body, int threads = 1)
{
    var samples = new List<double>();
    for (var round = 0; round < Rounds; round++)
    {
        GC.Collect();
        var clock = Stopwatch.StartNew();
        if (threads == 1)
            body(0);
        else
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, body);
        clock.Stop();
        samples.Add(clock.Elapsed.TotalMicroseconds / (Pairs * threads));
        RequestResponseLogger.Clear();
    }
    samples.Sort();
    return samples[samples.Count / 2];
}

void Pair(string testId) =>
    RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, uri, "Shop", "Tests", "{}", "{}");

void Calls(string testId)
{
    for (var i = 0; i < Pairs; i++)
        Pair(testId);
}

var noState = "no-step-ever";
Console.WriteLine($"no step state, one thread        {Measure(_ => Calls(noState)):F3}");

var closed = "steps-closed";
StepCollector.StartStep(closed, null, "done", null, null);
StepCollector.CompleteStep(closed, true);
Console.WriteLine($"state, no step open, one thread  {Measure(_ => Calls(closed)):F3}");

var open = "step-open";
StepCollector.StartStep(open, null, "open", null, null);
Console.WriteLine($"a step open, one thread          {Measure(_ => Calls(open)):F3}");

var shared = "shared-test";
StepCollector.StartStep(shared, null, "open", null, null);
Console.WriteLine($"a step open, 8 threads, one test {Measure(_ => Calls(shared), 8):F3}");

for (var t = 0; t < 8; t++)
    StepCollector.StartStep("own-" + t, null, "open", null, null);
Console.WriteLine($"a step open, 8 threads, own test {Measure(t => Calls("own-" + t), 8):F3}");
