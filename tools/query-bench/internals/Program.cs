using System.Diagnostics;
using Kronikol.Tool.Query;

// The in-process internals harness behind QUERY_PERF_PLAN.md section 1.3: calls the tool's real
// internals with per-stage time and allocation, warmed, so the steady-state cost of each stage is
// visible without the per-invocation JIT the CLI pays.
//
//   dotnet run -c Release --project tools/query-bench/internals -- <report.json> [reps]

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: internals <report.json> [reps]");
    return 2;
}

var path = args[0];
var reps = args.Length > 1 ? int.Parse(args[1]) : 3;

// Warmup rep first (JIT + page cache), then the measured reps; medians printed per stage.
var scanTimes = new List<double>();
var rawTimes = new List<double>();
var parseTimes = new List<double>();
var evalTimes = new List<double>();
long scanAllocated = 0;
var bodies = 0;
var opens = 0;

for (var rep = 0; rep < reps + 1; rep++)
{
    var warmup = rep == 0;

    var before = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    var index = ReportScanner.Scan(path);
    watch.Stop();
    if (!warmup)
    {
        scanTimes.Add(watch.Elapsed.TotalSeconds);
        scanAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
    }
    bodies = index.Bodies.Count;

    using var cache = new BodyCache(index);

    watch.Restart();
    foreach (var hash in index.Bodies.Keys)
        cache.Raw(hash);
    watch.Stop();
    if (!warmup)
        rawTimes.Add(watch.Elapsed.TotalSeconds);

    watch.Restart();
    foreach (var hash in index.Bodies.Keys)
        cache.Json(hash);
    watch.Stop();
    if (!warmup)
        parseTimes.Add(watch.Elapsed.TotalSeconds);

    // The values-shaped stage: one path evaluated over every parsed body.
    if (!PathEngine.TryParse("$.status", out var segments, out _))
        throw new InvalidOperationException("$.status did not parse");
    watch.Restart();
    var matches = 0;
    foreach (var hash in index.Bodies.Keys)
    {
        if (cache.Json(hash) is not { } document)
            continue;
        foreach (var _ in PathEngine.SelectAll(document.RootElement, segments))
            matches++;
    }
    watch.Stop();
    if (!warmup)
        evalTimes.Add(watch.Elapsed.TotalSeconds);

    opens = index.PayloadOpens;

    if (warmup)
        Console.WriteLine($"warmup done: {bodies:N0} distinct bodies, {matches:N0} matches for $.status, {opens:N0} payload opens");
}

Console.WriteLine($"scan            {Median(scanTimes):F2} s   ({scanAllocated / (1024.0 * 1024.0):F0} MB allocated)");
Console.WriteLine($"raw (all)       {Median(rawTimes):F2} s   ({bodies:N0} distinct bodies)");
Console.WriteLine($"parse (all)     {Median(parseTimes):F2} s");
Console.WriteLine($"eval $.status   {Median(evalTimes):F2} s");
Console.WriteLine($"payload opens per command: {opens:N0}");
return 0;

static double Median(List<double> times)
{
    var sorted = times.OrderBy(t => t).ToList();
    return sorted[sorted.Count / 2];
}
