#:package Kronikol@5.2.3
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// P2: the edges the design depends on, on a published package.
//   a. A run that wrote no end marker: its window stays open, and the next run's traffic is kept and folded into it.
//   b. An explicit window that ends before it starts.
//   c. The same suite run twice on one stack, with test ids that are stable across runs (Playwright's are): the first
//      run's calls carry the second run's ids, so "carries the id of one of this run's tests" does not prove a record is
//      this run's, and without the window those calls land in this run's tests.
//   d. A Phase boundary placed after a scenario's last call, with SeparateSetup on: does the diagram draw every call
//      inside the Setup partition? (The fold scenario carries it here, since a capture's raw marker is folded with it.)
//   dotnet run --file p2-edges.cs -- <empty folder>
using Kronikol.Ingestion;
using Kronikol.Tracking;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var dir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(dir);

const string day = "2026-10-21T";
string T(string time) => day + time + "Z";
var n = 0;
IEnumerable<string> Call(string at, string answeredAt, string testId, string service, string caller, string method, string uri)
{
    var id = $"c{++n}";
    yield return $$"""{"type":"Request","testId":"{{testId}}","testName":"{{testId}}","requestResponseId":"{{id}}","method":"{{method}}","uri":"{{uri}}","serviceName":"{{service}}","callerName":"{{caller}}","timestamp":"{{T(at)}}"}""";
    yield return $$"""{"type":"Response","testId":"{{testId}}","testName":"{{testId}}","requestResponseId":"{{id}}","method":"{{method}}","uri":"{{uri}}","serviceName":"{{service}}","callerName":"{{caller}}","statusCode":"OK","timestamp":"{{T(answeredAt)}}"}""";
}
string File_(string name, IEnumerable<string> lines) { var p = Path.Combine(dir, name); File.WriteAllLines(p, lines); return p; }
string Start(string id, string name, string at) => $$"""{"event":"start","testId":"{{id}}","testName":"{{name}}","feature":"Orders","timestamp":"{{T(at)}}"}""";
string End(string id, string at) => $$"""{"event":"end","testId":"{{id}}","status":"passed","timestamp":"{{T(at)}}"}""";
string Run(string status, string at) => $$"""{"event":"testrun","testId":"__run__","status":"{{status}}","timestamp":"{{T(at)}}"}""";

// a. The browser run never wrote its end marker (its process died); the device run follows on the same stack.
var captureA = File_("a.capture.ndjson", Call("10:00:02.000", "10:00:02.100", "b1", "Orders Api", "browser", "GET", "http://orders-api/orders")
    .Concat(Call("10:00:13.000", "10:00:13.100", "d1", "Orders Api", "device", "GET", "http://orders-api/orders"))
    .Concat(Call("10:00:13.050", "10:00:13.060", "session", "Database", "Orders Api", "GET", "db://orders/find?customer=2")));
var testsA = File_("a.tests.ndjson", [Run("started", "10:00:00.000"), Start("b1", "Lists orders", "10:00:01.000"), End("b1", "10:00:05.000")]);
Ingest("a. a run with no end marker, then another run", captureA, testsA, "a", r => r);

// b. An explicit window that ends before it starts.
Ingest("b. RunStartedAt after RunEndedAt", captureA, testsA, "b", r => With(r, startedAt: T("10:00:10.000"), endedAt: T("10:00:00.000")));

// c. The same suite twice: run 1 at 09:00, run 2 at 10:00, the same two test ids in both.
var captureC = File_("c.capture.ndjson", Call("09:00:02.000", "09:00:02.100", "t1", "Orders Api", "browser", "GET", "http://orders-api/orders?run=1")
    .Concat(Call("09:00:07.000", "09:00:07.100", "t2", "Orders Api", "browser", "GET", "http://orders-api/menu?run=1"))
    .Concat(Call("10:00:02.000", "10:00:02.100", "t1", "Orders Api", "browser", "GET", "http://orders-api/orders?run=2"))
    .Concat(Call("10:00:07.000", "10:00:07.100", "t2", "Orders Api", "browser", "GET", "http://orders-api/menu?run=2")));
var testsC = File_("c.tests.ndjson", [Run("started", "10:00:00.000"), Start("t1", "Lists orders", "10:00:01.000"), End("t1", "10:00:05.000"),
    Start("t2", "Shows the menu", "10:00:06.000"), End("t2", "10:00:10.000"), Run("passed", "10:00:11.000")]);
Ingest("c1. the same suite's second run, with the run window", captureC, testsC, "c1", r => r);
Ingest("c2. the same suite's second run, without it", captureC, testsC, "c2", r => With(r, window: false));

// d. Fold traffic followed by a raw Phase boundary, with SeparateSetup on.
var captureD = File_("d.capture.ndjson", Call("10:00:00.200", "10:00:00.300", "session", "Database", "seeder", "POST", "db://orders/insert?seed=1")
    .Concat(Call("10:00:00.400", "10:00:00.500", "session", "Database", "seeder", "POST", "db://orders/insert?seed=2"))
    .Concat([$$"""{"kind":"marker","markerKind":"Phase","type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","testId":"session","timestamp":"{{T("10:00:00.600")}}"}"""])
    .Concat(Call("10:00:02.000", "10:00:02.100", "b1", "Orders Api", "browser", "GET", "http://orders-api/orders")));
var testsD = File_("d.tests.ndjson", [Run("started", "10:00:00.000"), Start("b1", "Lists orders", "10:00:01.000"), End("b1", "10:00:05.000"), Run("passed", "10:00:06.000")]);
Ingest("d. a Phase boundary after the fold scenario's last call, SeparateSetup on", captureD, testsD, "d", r => r, separateSetup: true);

static IngestRequest With(IngestRequest r, string? startedAt = null, string? endedAt = null, bool window = true) => new()
{
    InteractionFiles = r.InteractionFiles, TestsFile = r.TestsFile, Options = r.Options, AttributeByTestWindow = r.AttributeByTestWindow,
    WindowAttributionFallbackId = r.WindowAttributionFallbackId, FoldUnknownTestsInto = r.FoldUnknownTestsInto, DropOutsideRunWindow = window,
    RunStartedAt = startedAt is null ? null : DateTimeOffset.Parse(startedAt), RunEndedAt = endedAt is null ? null : DateTimeOffset.Parse(endedAt),
};

void Ingest(string label, string capture, string tests, string outName, Func<IngestRequest, IngestRequest> shape, bool separateSetup = false)
{
    var options = IngestPipeline.DefaultOptions() with { SeparateSetup = separateSetup };
    options.ReportsFolderPath = Path.Combine(dir, "out-" + outName);
    var result = IngestPipeline.Run(shape(new IngestRequest
    {
        InteractionFiles = [capture], TestsFile = tests, Options = options, AttributeByTestWindow = true,
        WindowAttributionFallbackId = "session", FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"), DropOutsideRunWindow = true,
    }));
    Console.WriteLine($"== {label}");
    Console.WriteLine($"run: {result.Start:HH:mm:ss.fff} -> {result.End:HH:mm:ss.fff}");
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"  {diagnostic.Kind}: {diagnostic.Message}");
    var logs = RequestResponseLogger.RequestAndResponseLogs;
    foreach (var scenario in result.Features.SelectMany(f => f.Scenarios))
    {
        var uris = logs.Where(l => l.TestId == scenario.Id && l.Type == RequestResponseType.Request).Select(l => l.Uri.ToString());
        Console.WriteLine($"  {scenario.DisplayName} ({scenario.Id}): {string.Join(", ", uris)}");
    }
    Console.WriteLine($"  report: {options.ReportsFolderPath}");
    Console.WriteLine();
}
