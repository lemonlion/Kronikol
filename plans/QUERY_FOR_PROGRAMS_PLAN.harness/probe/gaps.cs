#:package Kronikol@5.19.0
#:property PublishAot=false
// The issue's repro at 5.19.0, plus: two tests with one name, a retry, a diagnostic naming its scenario, and the asks.
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var dir = args[0];
var tests = Path.Combine(dir, "tests.ndjson");
var calls = Path.Combine(dir, "calls.ndjson");
File.WriteAllLines(tests,
[
    """{"event":"start","testId":"0af7651916cd43dd8448eb211c80319c","testName":"lists orders","feature":"Orders","timestamp":"2026-01-01T10:00:00.000Z"}""",
    """{"event":"end","testId":"0af7651916cd43dd8448eb211c80319c","status":"passed","durationMs":1000,"timestamp":"2026-01-01T10:00:01.000Z"}""",
    """{"event":"start","testId":"b2","testName":"lists orders","feature":"Orders","timestamp":"2026-01-01T10:00:02.000Z"}""",
    """{"event":"end","testId":"b2","status":"failed","durationMs":1000,"timestamp":"2026-01-01T10:00:03.000Z","errorMessage":"boom"}""",
]);
File.WriteAllLines(calls,
[
    """{"type":"Request","testId":"0af7651916cd43dd8448eb211c80319c","testName":"lists orders","requestResponseId":"a","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"orders-ui","headers":[{"key":"x-debug-token","value":"needle-in-a-header"}],"timestamp":"2026-01-01T10:00:00.500Z"}""",
    """{"type":"Response","testId":"0af7651916cd43dd8448eb211c80319c","testName":"lists orders","requestResponseId":"a","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"orders-ui","statusCode":"OK","content":"[]","timestamp":"2026-01-01T10:00:00.510Z"}""",
    """{"type":"Request","testId":"b2","testName":"lists orders","requestResponseId":"b","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"admin-ui","timestamp":"2026-01-01T10:00:02.500Z"}""",
    """{"type":"Response","testId":"b2","testName":"lists orders","requestResponseId":"b","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"admin-ui","statusCode":"OK","content":"[]","timestamp":"2026-01-01T10:00:02.510Z"}""",
]);

var options = IngestPipeline.DefaultOptions();
options.ReportsFolderPath = Path.Combine(dir, "out");
IngestPipeline.Run(new IngestRequest
{
    InteractionFiles = [calls],
    TestsFile = tests,
    Options = options,
    HostDiagnostics =
    [
        new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-a: 3 oversize payload(s) streamed past"),
        new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-b: decoding disabled on 1 connection(s)"),
    ],
});

void Ask(params string[] command)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var exit = QueryCommand.Run([command[0], options.ReportsFolderPath, .. command[1..]], output, error);
    Console.WriteLine($"$ query {string.Join(' ', command)}   (exit {exit})");
    Console.WriteLine(output.ToString().TrimEnd());
    if (error.ToString().Trim() is { Length: > 0 } said)
        Console.WriteLine($"[stderr] {said}");
    Console.WriteLine();
}

Ask("grep", "needle-in-a-header", "--count");
Ask("summary", "--json");
Ask("scenarios", "--json");
Ask("interactions", "s0", "--json");
Ask("diagram", "s0/d0", "--out", Path.Combine(dir, "d0.puml"));
Ask("diagram", "s0/d1", "--out", Path.Combine(dir, "d1.puml"));
Ask("diagram", "s0", "--out", dir);
Ask("steps", "id:0af7651916cd43dd8448eb211c80319c");
Ask("steps", "s9");
Ask("bogusverb");
