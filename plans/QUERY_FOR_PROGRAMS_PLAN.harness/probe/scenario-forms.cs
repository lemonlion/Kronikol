#:package Kronikol@5.19.0
#:property PublishAot=false
// Which verbs take a scenario by sid: when the stableId is unique, and what compare does with a sid: or an
// interaction address. Two ingested tests with different names, so every stableId is unique.
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Query;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var dir = args[0];
var tests = Path.Combine(dir, "tests.ndjson");
var calls = Path.Combine(dir, "calls.ndjson");
File.WriteAllLines(tests,
[
    """{"event":"start","testId":"t-1","testName":"lists orders","feature":"Orders","timestamp":"2026-01-01T10:00:00.000Z"}""",
    """{"event":"end","testId":"t-1","status":"passed","durationMs":1000,"timestamp":"2026-01-01T10:00:01.000Z"}""",
    """{"event":"start","testId":"t-2","testName":"cancels an order","feature":"Orders","timestamp":"2026-01-01T10:00:02.000Z"}""",
    """{"event":"end","testId":"t-2","status":"passed","durationMs":1000,"timestamp":"2026-01-01T10:00:03.000Z"}""",
]);
File.WriteAllLines(calls,
[
    """{"type":"Request","testId":"t-1","requestResponseId":"a","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"orders-ui","timestamp":"2026-01-01T10:00:00.500Z"}""",
    """{"type":"Response","testId":"t-1","requestResponseId":"a","method":"GET","uri":"http://orders-api/orders","serviceName":"orders-api","callerName":"orders-ui","statusCode":"OK","content":"{\"status\":\"open\"}","timestamp":"2026-01-01T10:00:00.510Z"}""",
    """{"type":"Request","testId":"t-2","requestResponseId":"b","method":"DELETE","uri":"http://orders-api/orders/1","serviceName":"orders-api","callerName":"admin-ui","timestamp":"2026-01-01T10:00:02.500Z"}""",
    """{"type":"Response","testId":"t-2","requestResponseId":"b","method":"DELETE","uri":"http://orders-api/orders/1","serviceName":"orders-api","callerName":"admin-ui","statusCode":"OK","content":"{\"status\":\"cancelled\"}","timestamp":"2026-01-01T10:00:02.510Z"}""",
]);
var options = IngestPipeline.DefaultOptions();
options.ReportsFolderPath = Path.Combine(dir, "out");
IngestPipeline.Run(new IngestRequest { InteractionFiles = [calls], TestsFile = tests, Options = options });

string Run(params string[] command)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var exit = QueryCommand.Run([command[0], options.ReportsFolderPath, .. command[1..]], output, error);
    var said = error.ToString().Split('\n').FirstOrDefault(l => l.Trim().Length > 0 && !l.StartsWith('!'))?.Trim() ?? "";
    var first = output.ToString().Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
    Console.WriteLine($"$ query {string.Join(' ', command),-48} exit {exit}  {(said.Length > 0 ? "[stderr] " + said : first)}");
    return output.ToString();
}

using var listing = JsonDocument.Parse(Run("scenarios", "--json"));
var sid = listing.RootElement.GetProperty("items")[0].GetProperty("stableId").GetString();
Console.WriteLine($"s0 is sid:{sid}");
foreach (var verb in new[] { "steps", "flow", "interactions", "failures", "scenarios", "annotations", "attachments", "assertions", "repro", "services", "parties" })
    Run(verb, $"sid:{sid}");
Run("values", "--path", "$.status", $"sid:{sid}");
Run("values", "--path", "$.status", "s0");
Run("compare", $"sid:{sid}", "s1");
Run("compare", "s0", "s1");
Run("compare", "s0/i0", "s1");
Run("compare", "b:00000000", "s1");
Run("history", $"sid:{sid}");
