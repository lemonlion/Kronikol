using System.Text;
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// TEMPORARY probe for plans/INGEST_FIDELITY_PLAN.md (kept in plans/INGEST_FIDELITY_PLAN.harness/, deleted
/// from the test project after the run). It feeds the pipeline what an external capturer writes, as raw
/// NDJSON lines, and prints what the report makes of it to $KRONIKOL_PROBE_OUT. It asserts nothing.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestFidelityProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-fidelity-probe-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly StringBuilder Out = new();

    public IngestFidelityProbeTests() { Directory.CreateDirectory(_dir); RequestResponseLogger.Redaction = null; }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        var path = Environment.GetEnvironmentVariable("KRONIKOL_PROBE_OUT") ?? Path.Combine(Path.GetTempPath(), "fidelity-probe.txt");
        lock (Out) File.AppendAllText(path, Out.ToString());
        Out.Clear();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static string At(double ms) => T0.AddMilliseconds(ms).ToString("O");

    private static string Pair(string testId, string method, string path, string service, double startMs, double endMs, string status = "200", string? extraResponse = null) =>
        $$"""{"type":"Request","method":"{{method}}","uri":"http://localhost:5000{{path}}","serviceName":"{{service}}","callerName":"superpay-graphql","content":"{}","requestResponseId":"{{testId}}-{{path}}-{{startMs}}","timestamp":"{{At(startMs)}}","testId":"{{testId}}"}""" + "\n" +
        $$"""{"type":"Response","method":"{{method}}","uri":"http://localhost:5000{{path}}","serviceName":"{{service}}","callerName":"superpay-graphql","content":"{\"ok\":true}","statusCode":"{{status}}","requestResponseId":"{{testId}}-{{path}}-{{startMs}}","timestamp":"{{At(endMs)}}","testId":"{{testId}}"{{extraResponse}}}""" + "\n";

    private IngestResult Run(string name, string interactions, TestRunRecord[] tests, Action<ReportConfigurationOptions>? configure = null, bool phaseFromSteps = false)
    {
        var capture = Path.Combine(_dir, name + ".ndjson");
        File.WriteAllText(capture, interactions);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        configure?.Invoke(options);
        DefaultDiagramsFetcher.Reset();
        return IngestPipeline.Run(new IngestRequest { InteractionFiles = [capture], TestRecords = tests, Options = options, PhaseFromSteps = phaseFromSteps });
    }

    private JsonElement[] Scenarios(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, name, "TestRunReport.json")));
        return json.RootElement.GetProperty("features").EnumerateArray().SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).Select(s => s.Clone()).ToArray();
    }

    private static string Diagram(JsonElement scenario) => ReportPayloadText.Of(scenario.GetProperty("diagrams")[0]) ?? "";

    [Fact]
    public void P1_phase_from_steps_and_the_setup_partition()
    {
        var id = "p1";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "step", TestId = id, Text = "a saved card", Keyword = "Given", KeywordType = "Context", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(1000) },
            new() { Event = "step", TestId = id, Text = "the card is charged", Keyword = "When", KeywordType = "Action", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(3000) },
            new() { Event = "end", TestId = id, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
        ];
        var calls = Pair(id, "GET", "/cards/1", "cards", 1500, 1600) + Pair(id, "POST", "/charges", "psp", 3500, 3600);
        var phaseMarker = $$"""{"kind":"marker","markerKind":"Phase","type":"Request","uri":"http://override.com/","serviceName":"","callerName":"","testId":"{{id}}","timestamp":"{{At(3000)}}"}""" + "\n";

        foreach (var (name, input, separate, fromSteps) in new[]
        {
            ("p1-separate-only", calls, true, false),
            ("p1-separate-and-phase-from-steps", calls, true, true),
            ("p1-separate-and-phase-marker", calls + phaseMarker, true, false),
            ("p1-phase-from-steps-cli-defaults", calls, false, true),
        })
        {
            Run(name, input, tests, o => o.SeparateSetup = separate, fromSteps);
            var s = Scenarios(name).Single(x => x.GetProperty("id").GetString() == id);
            var phases = string.Join(",", s.GetProperty("httpInteractions").EnumerateArray().Select(i => i.TryGetProperty("phase", out var p) ? p.ToString() : "<absent>"));
            var diagram = Diagram(s);
            lock (Out) Out.AppendLine($"P1 {name}: SeparateSetup={separate} PhaseFromSteps={fromSteps} -> partition={diagram.Contains("partition", StringComparison.Ordinal)} phases=[{phases}]");
        }
    }

    [Fact]
    public void P2_a_failed_call_from_an_external_capturer()
    {
        var id = "p2";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "reports a provider outage", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "end", TestId = id, Status = "failed", Error = "expected 502, received 500", DurationMs = 3000, Timestamp = T0.AddMilliseconds(3000) },
        ];
        // (a) the in-process shape: the failure half with "!Type" as the status and the message chain in "error";
        // (b) a capturer that put an object in "error" and no status, which today's reader ignores.
        var a = Pair(id, "POST", "/charges", "psp", 1000, 1100, status: "!TypeError", extraResponse: ",\"error\":\"fetch failed Caused by: connect ECONNREFUSED 127.0.0.1:443\"");
        var b = Pair(id, "GET", "/risk/1", "risk", 2000, 2100, status: "", extraResponse: ",\"error\":{\"code\":\"ECONNRESET\"}").Replace(",\"statusCode\":\"\"", "");
        var result = Run("p2", a + b, tests);
        var s = Scenarios("p2").Single(x => x.GetProperty("id").GetString() == id);
        var diagram = Diagram(s);
        lock (Out)
        {
            foreach (var i in s.GetProperty("httpInteractions").EnumerateArray())
                Out.AppendLine($"P2 {i.GetProperty("type")} {i.GetProperty("uri")}: statusCode={i.GetProperty("statusCode").GetRawText()} statusText={i.GetProperty("statusText").GetRawText()} error={i.GetProperty("error").GetRawText()}");
            Out.AppendLine($"  diagram draws '!TypeError': {diagram.Contains("!TypeError", StringComparison.Ordinal)}");
            var failures = File.ReadAllText(Path.Combine(_dir, "p2", "Failures.md"));
            Out.AppendLine($"  Failures.md names '!TypeError': {failures.Contains("!TypeError", StringComparison.Ordinal)}; names 'ECONNREFUSED': {failures.Contains("ECONNREFUSED", StringComparison.Ordinal)}");
            Out.AppendLine("  diagnostics: " + string.Join(" || ", result.Diagnostics.Select(x => x.Kind + ": " + x.Message)));
        }
    }

    [Fact]
    public void P3_a_retried_test_in_the_tests_file()
    {
        var id = "p3";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "step", TestId = id, Text = "attempt one", Timestamp = T0.AddMilliseconds(500) },
            new() { Event = "end", TestId = id, Status = "failed", Error = "expected SETTLED, received PENDING", DurationMs = 2000, Timestamp = T0.AddMilliseconds(2000) },
            new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0.AddMilliseconds(3000) },
            new() { Event = "step", TestId = id, Text = "attempt two", Timestamp = T0.AddMilliseconds(3500) },
            new() { Event = "end", TestId = id, Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(5000) },
        ];
        var calls = Pair(id, "POST", "/charges", "psp", 1000, 1100) + Pair(id, "POST", "/charges", "psp", 4000, 4100);
        var result = Run("p3", calls, tests);
        var scenarios = Scenarios("p3").Where(x => x.GetProperty("id").GetString() == id).ToArray();
        lock (Out)
        {
            Out.AppendLine($"P3 retried test: scenarios with id={scenarios.Length}");
            foreach (var s in scenarios)
            {
                var steps = s.TryGetProperty("steps", out var st) && st.ValueKind == JsonValueKind.Array ? string.Join(" | ", st.EnumerateArray().Select(x => x.GetProperty("text").GetString())) : "<none>";
                var calls2 = s.GetProperty("httpInteractions").GetArrayLength();
                var res = s.TryGetProperty("result", out var r) ? r.ToString() : "?";
                var dur = s.TryGetProperty("duration", out var d) ? d.ToString() : (s.TryGetProperty("durationMs", out var dm) ? dm.ToString() : "?");
                var err = s.TryGetProperty("errorMessage", out var e) ? e.ToString() : "<none>";
                string V(string m) => s.TryGetProperty(m, out var v) ? v.GetRawText() : "<absent>";
                Out.AppendLine($"  durationSeconds={V("durationSeconds")} attempt={V("attempt")} sourceFile={V("sourceFile")} sourceLine={V("sourceLine")} failureCause={V("failureCause")} exampleDisplayName={V("exampleDisplayName")}");
                Out.AppendLine($"  result={res} duration={dur} steps=[{steps}] httpInteractions={calls2} error={err}");
                Out.AppendLine("  members: " + string.Join(",", s.EnumerateObject().Select(p => p.Name)));
            }
            var failures = Path.Combine(_dir, "p3", "Failures.md");
            Out.AppendLine("  Failures.md first line: " + (File.Exists(failures) ? File.ReadLines(failures).FirstOrDefault() : "<absent>"));
            Out.AppendLine("  diagnostics: " + string.Join(" || ", result.Diagnostics.Select(x => x.Kind + ": " + x.Message)));
        }

        // The workaround a harness has today: one test id per attempt.
        TestRunRecord[] split =
        [
            new() { Event = "start", TestId = "p3a1", TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "end", TestId = "p3a1", Status = "failed", Error = "expected SETTLED, received PENDING", DurationMs = 2000, Timestamp = T0.AddMilliseconds(2000) },
            new() { Event = "start", TestId = "p3a2", TestName = "charges a card", Feature = "charge.test.ts", Timestamp = T0.AddMilliseconds(3000) },
            new() { Event = "end", TestId = "p3a2", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(5000) },
        ];
        Run("p3split", Pair("p3a1", "POST", "/charges", "psp", 1000, 1100) + Pair("p3a2", "POST", "/charges", "psp", 4000, 4100), split);
        var both = Scenarios("p3split");
        lock (Out)
        {
            Out.AppendLine($"P3 split ids: scenarios={both.Length} results=[{string.Join(",", both.Select(x => x.GetProperty("name").GetString() + ":" + x.GetProperty("result")))}]");
            Out.AppendLine("  Failures.md first line: " + File.ReadLines(Path.Combine(_dir, "p3split", "Failures.md")).FirstOrDefault());
        }
    }
}
