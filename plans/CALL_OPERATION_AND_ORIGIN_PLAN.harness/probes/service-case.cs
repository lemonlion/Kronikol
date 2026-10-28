#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// `services` and `interactions --group-by service` over two calls whose service names differ only in case.
// Run from this folder: dotnet run --file service-case.cs -- <empty folder>
using Kronikol.Ingestion;
using Kronikol.Query;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
var dir = args[0];
var tests = Path.Combine(dir, "tests.ndjson");
var calls = Path.Combine(dir, "calls.ndjson");
const string id = "0af7651916cd43dd8448eb211c80319c";
File.WriteAllLines(tests,
[
    $$"""{"event":"start","testId":"{{id}}","testName":"lists orders","feature":"Orders","timestamp":"2026-01-01T10:00:00.000Z"}""",
    $$"""{"event":"end","testId":"{{id}}","status":"passed","durationMs":1000,"timestamp":"2026-01-01T10:00:01.000Z"}""",
]);
string Call(string rr, string service, string type, string status, string content, string at) =>
    $$"""{"type":"{{type}}","testId":"{{id}}","testName":"lists orders","requestResponseId":"{{rr}}","method":"GET","uri":"http://orders/orders/1","serviceName":"{{service}}","callerName":"Caller"{{(status.Length > 0 ? $",\"statusCode\":\"{status}\"" : "")}},"content":"{{content}}","timestamp":"{{at}}"}""";
File.WriteAllLines(calls,
[
    Call("a", "Orders", "Request", "", "", "2026-01-01T10:00:00.100Z"),
    Call("a", "Orders", "Response", "OK", "[1,2,3]", "2026-01-01T10:00:00.110Z"),
    Call("b", "orders", "Request", "", "", "2026-01-01T10:00:00.200Z"),
    Call("b", "orders", "Response", "OK", "[4,5,6,7,8]", "2026-01-01T10:00:00.210Z"),
]);

var options = IngestPipeline.DefaultOptions();
options.ReportsFolderPath = Path.Combine(dir, "out");
IngestPipeline.Run(new IngestRequest { InteractionFiles = [calls], TestsFile = tests, Options = options });

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

Ask("services");
Ask("interactions", "--group-by", "service");
