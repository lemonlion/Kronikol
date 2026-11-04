// What a read of TestPhaseContext.Current costs, and what a whole tracked call costs, on one version. Release, one process per version.
using System.Diagnostics;
using Kronikol;
using Kronikol.Tracking;

static double NsPerRead(int reads)
{
    var sink = 0;
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < reads; i++)
        sink += (int)TestPhaseContext.Current;
    watch.Stop();
    GC.KeepAlive(sink);
    return watch.Elapsed.TotalNanoseconds / reads;
}

static double Best(Func<double> run, int times = 5) { var best = double.MaxValue; for (var i = 0; i < times; i++) best = Math.Min(best, run()); return best; }

Console.WriteLine($"Kronikol {typeof(TestPhaseContext).Assembly.GetName().Version} on {Environment.Version}");
NsPerRead(2_000_000);

Track.TestIdResolver = null;
Console.WriteLine($"no resolver:                 {Best(() => NsPerRead(2_000_000)),9:F0} ns a read");

Track.TestIdResolver = () => null;
Console.WriteLine($"resolver answers null:       {Best(() => NsPerRead(2_000_000)),9:F0} ns a read");

const string id = "perf-test";
Track.TestIdResolver = () => id;
Console.WriteLine($"a test, no step yet:         {Best(() => NsPerRead(2_000_000)),9:F0} ns a read");

StepCollector.StartStep(id, "Given", "arrange", null, null);
Console.WriteLine($"a test, in a Given:          {Best(() => NsPerRead(2_000_000)),9:F0} ns a read");
StepCollector.CompleteStep(id, true);
StepCollector.StartStep(id, "When", "act", null, null);
StepCollector.CompleteStep(id, true);
Console.WriteLine($"a test, after a When:        {Best(() => NsPerRead(2_000_000)),9:F0} ns a read");

Track.TestIdResolver = () => throw new InvalidOperationException("Not in a scenario");
Console.WriteLine($"resolver throws, as LightBDD's does on a server's flow: {Best(() => NsPerRead(2_000)),9:F0} ns a read");

// A whole tracked call, on the same in-memory handler, with the test in its action.
Track.TestIdResolver = () => id;
var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
{
    CallerName = "Perf", FixedNameForReceivingService = "Shop", CurrentTestInfoFetcher = () => ("perf", id)
}) { InnerHandler = new Answer() });
async Task<double> UsPerCall(int calls)
{
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < calls; i++)
        (await client.GetAsync("http://perf.local/x")).Dispose();
    return watch.Elapsed.TotalMicroseconds / calls;
}
await UsPerCall(2_000);
var bestCall = double.MaxValue;
for (var i = 0; i < 5; i++) bestCall = Math.Min(bestCall, await UsPerCall(5_000));
Console.WriteLine($"a whole tracked HTTP call:   {bestCall,9:F1} us a call");

sealed class Answer : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") });
}
