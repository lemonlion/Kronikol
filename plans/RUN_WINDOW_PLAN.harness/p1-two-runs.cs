#:package Kronikol@5.2.3
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// P1: issue #187's shape on a published package. One capture that outlives two runs (a stack's start-up, a browser run,
// then a device run 0.6 s after the browser run's verdict), one tests file per run, each ingested with
// DropOutsideRunWindow, window attribution and a fold, as the issue's host does. Prints what each report keeps, what it
// says it dropped, and the run's start and end, then the two workarounds a host has today.
//   dotnet run --file p1-two-runs.cs -- <empty folder>
using Kronikol.Ingestion;
using Kronikol.Tracking;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var dir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(dir);

const string day = "2026-10-21T";
string T(string time) => day + time + "Z";
var calls = new List<string>();
var n = 0;
void Call(string at, string answeredAt, string testId, string service, string caller, string method, string uri)
{
    var id = $"c{++n}";
    calls.Add($$"""{"type":"Request","testId":"{{testId}}","testName":"{{testId}}","requestResponseId":"{{id}}","method":"{{method}}","uri":"{{uri}}","serviceName":"{{service}}","callerName":"{{caller}}","timestamp":"{{T(at)}}"}""");
    calls.Add($$"""{"type":"Response","testId":"{{testId}}","testName":"{{testId}}","requestResponseId":"{{id}}","method":"{{method}}","uri":"{{uri}}","serviceName":"{{service}}","callerName":"{{caller}}","statusCode":"OK","timestamp":"{{T(answeredAt)}}"}""");
}

// The stack's start-up, 13 minutes before the first run: a seeder, then the stack's own checks that its API is up.
for (var i = 0; i < 6; i++)
    Call($"09:47:0{i}.000", $"09:47:0{i}.050", "session", "Database", "seeder", "POST", $"db://orders/insert?seed={i}");
Call("09:47:20.000", "09:47:20.010", "session", "Orders Api", "compose", "GET", "http://orders-api/health");
Call("09:47:25.000", "09:47:25.010", "session", "Orders Api", "compose", "GET", "http://orders-api/health");

// The browser run: a global login before its first test, then two tests whose own calls carry their ids (a proxy tap
// reads the test header) and whose database calls carry the tap's fallback id.
Call("10:00:00.500", "10:00:00.600", "session", "Orders Api", "browser", "POST", "http://orders-api/login");
Call("10:00:02.000", "10:00:02.300", "b1", "Orders Api", "browser", "GET", "http://orders-api/orders");
Call("10:00:02.100", "10:00:02.200", "session", "Database", "Orders Api", "GET", "db://orders/find?customer=1");
Call("10:00:07.000", "10:00:07.300", "b2", "Orders Api", "browser", "GET", "http://orders-api/menu");
Call("10:00:07.100", "10:00:07.200", "session", "Database", "Orders Api", "GET", "db://menu/find");
// Two real losses: b2's own call made after the run's verdict, and traffic between the runs that no report covers.
Call("10:00:11.300", "10:00:11.350", "b2", "Orders Api", "browser", "POST", "http://orders-api/audit");
Call("10:00:11.400", "10:00:11.450", "session", "Database", "Orders Api", "GET", "db://audit/find");

// The device run, 0.6 s after the browser run's verdict, on the same stack.
Call("10:00:13.000", "10:00:13.300", "d1", "Orders Api", "device", "GET", "http://orders-api/orders");
Call("10:00:13.100", "10:00:13.200", "session", "Database", "Orders Api", "GET", "db://orders/find?customer=2");
Call("10:00:15.000", "10:00:15.300", "d1", "Orders Api", "device", "GET", "http://orders-api/menu");

var capture = Path.Combine(dir, "capture.ndjson");
File.WriteAllLines(capture, calls);

var browser = Path.Combine(dir, "browser.tests.ndjson");
File.WriteAllLines(browser,
[
    $$"""{"event":"testrun","testId":"__run__","status":"started","timestamp":"{{T("10:00:00.000")}}"}""",
    $$"""{"event":"start","testId":"b1","testName":"Lists orders","feature":"Orders","timestamp":"{{T("10:00:01.000")}}"}""",
    $$"""{"event":"end","testId":"b1","status":"passed","durationMs":4000,"timestamp":"{{T("10:00:05.000")}}"}""",
    $$"""{"event":"start","testId":"b2","testName":"Shows the menu","feature":"Orders","timestamp":"{{T("10:00:06.000")}}"}""",
    $$"""{"event":"end","testId":"b2","status":"passed","durationMs":4000,"timestamp":"{{T("10:00:10.000")}}"}""",
    $$"""{"event":"testrun","testId":"__run__","status":"passed","timestamp":"{{T("10:00:11.000")}}"}""",
]);
var device = Path.Combine(dir, "device.tests.ndjson");
File.WriteAllLines(device,
[
    $$"""{"event":"testrun","testId":"__run__","status":"started","timestamp":"{{T("10:00:11.600")}}"}""",
    $$"""{"event":"start","testId":"d1","testName":"Orders on a phone","feature":"Device","timestamp":"{{T("10:00:12.000")}}"}""",
    $$"""{"event":"end","testId":"d1","status":"passed","durationMs":8000,"timestamp":"{{T("10:00:20.000")}}"}""",
    $$"""{"event":"testrun","testId":"__run__","status":"passed","timestamp":"{{T("10:00:21.000")}}"}""",
]);

Ingest("A. browser run, as the issue's host ingests it", browser, "a", r => r);
Ingest("B. device run, as the issue's host ingests it", device, "b", r => r);
Ingest("C. browser run, window started at the stack's start to keep the start-up (workaround 1)", browser, "c",
    r => new IngestRequest
    {
        InteractionFiles = r.InteractionFiles, TestsFile = r.TestsFile, Options = r.Options, AttributeByTestWindow = true,
        WindowAttributionFallbackId = "session", FoldUnknownTestsInto = r.FoldUnknownTestsInto, DropOutsideRunWindow = true,
        RunStartedAt = DateTimeOffset.Parse(T("09:47:00.000")),
    });
Ingest("D. browser run, start-up dropped by a predicate instead (workaround 2, DropUnattributed on the database)", browser, "d",
    r => new IngestRequest
    {
        InteractionFiles = r.InteractionFiles, TestsFile = r.TestsFile, Options = r.Options, AttributeByTestWindow = true,
        WindowAttributionFallbackId = "session", FoldUnknownTestsInto = r.FoldUnknownTestsInto, DropOutsideRunWindow = true,
        RunStartedAt = DateTimeOffset.Parse(T("09:47:00.000")),
        DropUnattributed = record => record.ServiceName == "Database",
    });

void Ingest(string label, string testsFile, string outName, Func<IngestRequest, IngestRequest> shape)
{
    var options = IngestPipeline.DefaultOptions();
    options.ReportsFolderPath = Path.Combine(dir, "out-" + outName);
    var request = shape(new IngestRequest
    {
        InteractionFiles = [capture],
        TestsFile = testsFile,
        Options = options,
        AttributeByTestWindow = true,
        WindowAttributionFallbackId = "session",
        FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        DropOutsideRunWindow = true,
    });
    var result = IngestPipeline.Run(request);
    Console.WriteLine($"== {label}");
    Console.WriteLine($"run: {result.Start:HH:mm:ss.fff} -> {result.End:HH:mm:ss.fff} ({(result.End - result.Start).TotalSeconds:0.###} s)");
    Console.WriteLine($"diagnostics: {result.Diagnostics.Count}");
    foreach (var diagnostic in result.Diagnostics)
        Console.WriteLine($"  {diagnostic.Kind}: {diagnostic.Message}");
    var logs = RequestResponseLogger.RequestAndResponseLogs;
    Console.WriteLine("scenarios, in the order the report lists them:");
    foreach (var feature in result.Features)
    foreach (var scenario in feature.Scenarios)
    {
        var mine = logs.Where(l => l.TestId == scenario.Id).ToArray();
        var uris = string.Join(", ", mine.Where(l => l.Type == RequestResponseType.Request).Select(l => l.Uri.ToString()));
        Console.WriteLine($"  [{feature.DisplayName}] {scenario.DisplayName} (id {scenario.Id}{(scenario.NotATest ? ", not a test" : "")}, {scenario.Result}): {mine.Length} record(s): {uris}");
    }
    var elsewhere = logs.Where(l => !result.Features.SelectMany(f => f.Scenarios).Any(s => s.Id == l.TestId)).ToArray();
    if (elsewhere.Length > 0)
        Console.WriteLine($"  in no scenario: {elsewhere.Length} record(s) under {string.Join(", ", elsewhere.Select(l => l.TestId).Distinct())}");
    Console.WriteLine();
}
