using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

// Synthesizes the QUERY_PERF_PLAN.md benchmark report: 200 scenarios x 60 request/response pairs
// (24,000 interaction entries), every body distinct so distinct-body dedup wins nothing, responses
// ~6 KB. Lands at roughly 130 MB of TestRunReport.json.
//
//   dotnet run -c Release --project tools/query-bench/gen -- [output-path]

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "TestRunReport.query-bench.json");

var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
var logs = new List<RequestResponseLog>();
var features = new List<Feature>();

// ~100 filler fields land a response body around 8 KB and the whole file around 130 MB.
var filler = string.Join(",", Enumerable.Range(0, 100).Select(i =>
    $"\"field{i:D2}\":\"value value value value value value value value value value value {i:D4}\""));

for (var s = 0; s < 200; s++)
{
    var id = $"t{s}";
    features.Add(new Feature
    {
        DisplayName = $"Feature {s / 10}",
        Scenarios = [new Scenario { Id = id, DisplayName = $"Scenario {s}", Result = ExecutionResult.Passed }]
    });

    for (var i = 0; i < 60; i++)
    {
        var n = s * 60 + i;
        var at = start.AddMilliseconds(n * 3);
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        // Unique on both halves - request AND response - the worst case for a body cache.
        var request = $"{{\"scenario\":{s},\"attempt\":{i},\"unique\":\"req-{n:D6}\"}}";
        var response = $"{{\"n\":{n},\"status\":\"{(n % 7 == 0 ? "DECLINED" : "APPROVED")}\",\"display\":\"{n:N2}\",\"unique\":\"resp-{n:D6}\",{filler}}}";

        logs.Add(new RequestResponseLog(id, id, HttpMethod.Post, request, new Uri("http://payments/charge"),
            [("accept", "application/json")], "payments", "test", RequestResponseType.Request, traceId, pairId, false)
        { Timestamp = at });
        logs.Add(new RequestResponseLog(id, id, HttpMethod.Post, response, new Uri("http://payments/charge"),
            [], "payments", "test", RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.OK)
        { Timestamp = at.AddMilliseconds(2) });
    }
}

Console.WriteLine($"Generating: {features.Count} features, {logs.Count} interaction entries...");
var written = ReportGenerator.GenerateTestRunReportData(
    [.. features],
    new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
    new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
    "QueryBench_" + Guid.NewGuid().ToString("N")[..8] + ".json",
    DataFormat.Json, diagrams: null, trackedLogs: [.. logs]);

File.Move(written, output, overwrite: true);
Console.WriteLine($"{output}  ({new FileInfo(output).Length / (1024.0 * 1024.0):F1} MB)");
