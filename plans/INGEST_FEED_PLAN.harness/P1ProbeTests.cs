using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kronikol.Ingestion;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

// TEMPORARY PROBE for plans/INGEST_FEED_PLAN.md research. Deleted after the run.
[Collection("DiagramsFetcher")]
public class P1ProbeTests
{
    private static readonly string Out = Path.Combine(Path.GetTempPath(), "p1probe.txt");
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Probe()
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);
        try { RunProbe(L); }
        catch (Exception ex) { L("PROBE THREW: " + ex); }
        finally { File.WriteAllText(Out, sb.ToString()); }
    }

    private static void RunProbe(Action<string> L)
    {
        const string testId = "0af7651916cd43dd8448eb211c80319c";

        // ---------- 1. member-by-member diff of a fully populated log through FromLog → JSON → ToLog
        L("## 1. Member diff");
        var full = new RequestResponseLog("Overview renders", testId, HttpMethod.Post, "{\"q\":1}",
            new Uri("http://localhost:8081/sidekick?x=1"), [("Content-Type", "application/json")], "graphql", "web",
            RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), true, HttpStatusCode.Created, RequestResponseMetaType.Event, "AI", "User")
        {
            NoteOnRight = true, MarkerKind = DiagramMarkerKind.Row, PlantUml = "note over api : x", FocusFields = ["a", "b"], Timestamp = T0,
            AttributionSource = AttributionSource.Scope, ExpiredFromTestId = "expired-from", Error = "boom\nCaused by: inner",
            ActivitySpanId = "b7ad6b7169203331", ActivityTraceId = testId, Phase = TestPhase.Setup,
            SetupVariant = new PhaseVariant(HttpMethod.Get, new Uri("http://a/setup"), "sc", [], false),
            ActionVariant = new PhaseVariant(HttpMethod.Put, new Uri("http://a/action"), "ac", [], true),
            CollapsedCount = 3, CollapsedSummary = "12–48 ms", CapturedBy = "wire", DurationMs = 123.4,
        };
        var fullJson = InteractionRecord.FromLog(full).ToJson();
        L("JSON: " + fullJson);
        var back = InteractionRecord.FromJson(fullJson).ToLog();
        L("| Member | Before | After | Verdict |");
        L("|---|---|---|---|");
        foreach (var p in typeof(RequestResponseLog).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").OrderBy(p => p.Name))
        {
            var a = Show(p.GetValue(full));
            var b = Show(p.GetValue(back));
            L($"| `{p.Name}` | {a} | {b} | {(a == b ? "same" : "LOST")} |");
        }
        var wire = typeof(InteractionRecord).GetProperties().Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name).Where(n => n != null).ToArray();
        L($"InteractionRecord wire members ({wire.Length}): {string.Join(", ", wire)}");
        L($"RequestResponseLog public members: {typeof(RequestResponseLog).GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(p => p.Name != "EqualityContract")}");

        // ---------- 2. what the writer emits for each marker kind, and what ingest reads back
        L("");
        L("## 2. Marker logs through FromLog and back");
        var markers = new (string Name, RequestResponseLog Log)[]
        {
            ("custom start", Marker(testId, DiagramMarkerKind.Custom, "note over api : cache warmed", start: true, T0.AddSeconds(1))),
            ("custom end", Marker(testId, DiagramMarkerKind.Custom, null, start: false, T0.AddSeconds(1))),
            ("row start", Marker(testId, DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3", start: true, T0.AddSeconds(3))),
            ("step start", Marker(testId, DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>Given a basket", start: true, T0.AddSeconds(4))),
            ("phase", new RequestResponseLog("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = T0.AddSeconds(3.5) }),
        };
        foreach (var (name, log) in markers)
        {
            var json = InteractionRecord.FromLog(log).ToJson();
            L($"{name}: {json}");
            var logs = InteractionRecord.FromJson(json).ToLogs().ToArray();
            foreach (var l in logs)
                L($"   -> {logs.Length} log(s); IsOverrideStart={l.IsOverrideStart} IsOverrideEnd={l.IsOverrideEnd} IsActionStart={l.IsActionStart} IsDiagramMarker={l.IsDiagramMarker} MarkerKind={l.MarkerKind} PlantUml={Show(l.PlantUml)} Service={Show(l.ServiceName)} Caller={Show(l.CallerName)} Uri={l.Uri} Method={Show(l.Method.Value)}");
        }

        // ---------- 3. a whole in-process capture written through the writer, then ingested
        L("");
        L("## 3. Ingest of a writer-produced file (passed run)");
        var root = Directory.CreateTempSubdirectory("kronikol-p1probe-").FullName;
        var path = Path.Combine(root, "capture.ndjson");
        var traceA = Guid.NewGuid(); var rrA = Guid.NewGuid();
        var traceB = Guid.NewGuid(); var rrB = Guid.NewGuid();
        var logsInOrder = new List<RequestResponseLog>
        {
            markers[0].Log, markers[1].Log,
            new("Probe", testId, HttpMethod.Post, "{}", new Uri("http://localhost:8081/sidekick"), [("Content-Type", "application/json")], "graphql", "web", RequestResponseType.Request, traceA, rrA, false) { Timestamp = T0.AddSeconds(2) },
            new("Probe", testId, HttpMethod.Post, "{\"data\":{}}", new Uri("http://localhost:8081/sidekick"), [], "graphql", "web", RequestResponseType.Response, traceA, rrA, false, HttpStatusCode.OK) { Timestamp = T0.AddSeconds(2.05), DurationMs = 77 },
            markers[2].Log, Marker(testId, DiagramMarkerKind.Row, null, start: false, T0.AddSeconds(3)),
            markers[4].Log,
            markers[3].Log, Marker(testId, DiagramMarkerKind.Step, null, start: false, T0.AddSeconds(4)),
            new("Probe", testId, HttpMethod.Get, null, new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Request, traceB, rrB, false) { Timestamp = T0.AddSeconds(5), DurationMs = 5 },
            new("Probe", testId, HttpMethod.Get, "{\"ok\":true}", new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Response, traceB, rrB, false, HttpStatusCode.OK) { Timestamp = T0.AddSeconds(5.005), DurationMs = 5 },
        };
        using (var writer = new NdjsonInteractionWriter(path))
            foreach (var log in logsInOrder)
                writer.Log(log);
        L($"lines written: {File.ReadAllLines(path).Length}");

        var passedDir = Path.Combine(root, "passed");
        var result = Ingest(path, passedDir, testId, "passed");
        L($"Generated={result.Generated} scenarios={result.ScenarioCount} interactions={result.InteractionCount}");
        L("diagnostics: " + string.Join(" | ", result.Diagnostics.Select(d => d.ToString())));
        var reportJson = File.ReadAllText(Path.Combine(passedDir, "TestRunReport.json"));
        L($"'override.com' occurrences in TestRunReport.json: {Count(reportJson, "override.com")}");
        foreach (var ctx in Contexts(reportJson, "override.com", 3))
            L("   …" + ctx + "…");
        using (var doc = JsonDocument.Parse(reportJson))
        {
            var scenario = FindWith(doc.RootElement, "httpInteractions");
            if (scenario is { } s)
            {
                L("httpInteractions:");
                foreach (var i in s.GetProperty("httpInteractions").EnumerateArray())
                    L($"   type={Str(i, "type")} method={Str(i, "method")} uri={Str(i, "uri")} service={Str(i, "serviceName")} caller={Str(i, "callerName")} durationMs={Str(i, "durationMs")} stepPath={Str(i, "stepPath")}");
                L("annotations: " + s.GetProperty("annotations").GetRawText());
                if (s.TryGetProperty("steps", out var steps)) L("steps: " + steps.GetRawText());
            }
            else L("no object with httpInteractions found");
        }
        var puml = File.Exists(Path.Combine(passedDir, "TestRunReport.html")) ? File.ReadAllText(Path.Combine(passedDir, "TestRunReport.html")) : "";
        L($"'override.com' occurrences in TestRunReport.html: {Count(puml, "override.com")}");
        L($"'cache warmed' in html: {Count(puml, "cache warmed")}; 'Row 3' in html: {Count(puml, "Row 3")}; 'Given a basket' in html: {Count(puml, "Given a basket")}");
        L("outputs (passed): " + Sizes(passedDir));

        // ---------- 4. the same file ingested with a failed verdict
        L("");
        L("## 4. Same file, failed run");
        var failedDir = Path.Combine(root, "failed");
        var failed = Ingest(path, failedDir, testId, "failed");
        L($"Generated={failed.Generated}; outputs (failed): " + Sizes(failedDir));

        // ---------- 5. --render local through the CLI
        L("");
        L("## 5. --render local through IngestCommand.Run");
        var localDir = Path.Combine(root, "local");
        var @out = new StringWriter(); var err = new StringWriter();
        try
        {
            var exit = IngestCommand.Run([path, "-o", localDir, "--render", "local"], @out, err);
            L($"exit={exit}");
            L("stderr: " + err.ToString().Trim());
            L("stdout tail: " + string.Join(" / ", @out.ToString().Split('\n').TakeLast(4).Select(x => x.Trim())));
            L("outputs (local): " + (Directory.Exists(localDir) ? Sizes(localDir) : "no directory"));
        }
        catch (Exception ex)
        {
            L($"THREW {ex.GetType().FullName}: {ex.Message}");
            L("stderr so far: " + err.ToString().Trim());
            L("outputs (local): " + (Directory.Exists(localDir) ? Sizes(localDir) : "no directory"));
        }
        L("");
        L("root: " + root);
    }

    private static IngestResult Ingest(string path, string dir, string testId, string status)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = dir;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [path],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = testId, TestName = "probe › round trip", Feature = "probe.feature", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = testId, Status = status, DurationMs = 6000, Timestamp = T0.AddSeconds(6) },
            ],
            Options = options,
        });
    }

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string? plantUml, bool start, DateTimeOffset at) =>
        new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = start, IsOverrideEnd = !start, MarkerKind = kind,
            PlantUml = plantUml is null ? null : $"\n{plantUml}\n\n", Timestamp = at,
        };

    private static string Sizes(string dir) => string.Join(", ",
        Directory.EnumerateFiles(dir).Select(f => $"{Path.GetFileName(f)}={new FileInfo(f).Length}B").OrderBy(x => x));

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ToString() : "<absent>";

    private static JsonElement? FindWith(JsonElement e, string property)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty(property, out _)) return e;
            foreach (var p in e.EnumerateObject())
                if (FindWith(p.Value, property) is { } hit) return hit;
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                if (FindWith(item, property) is { } hit) return hit;
        return null;
    }

    private static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static IEnumerable<string> Contexts(string text, string needle, int max)
    {
        int i = 0, n = 0;
        while (n < max && (i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            var from = Math.Max(0, i - 80); var to = Math.Min(text.Length, i + needle.Length + 80);
            yield return text[from..to].Replace('\n', ' ');
            i += needle.Length; n++;
        }
    }

    private static string Show(object? v) => v switch
    {
        null => "null",
        string s => "\"" + s.Replace("\n", "\\n") + "\"",
        Array a => "[" + string.Join(",", a.Cast<object?>().Select(Show)) + "]",
        ITuple t => "(" + string.Join(",", Enumerable.Range(0, t.Length).Select(i => Show(t[i]))) + ")",
        PhaseVariant pv => $"PhaseVariant({Show(pv.Method.Value)},{pv.Uri},{Show(pv.Content)},skip={pv.Skip})",
        _ when v.GetType().Name.StartsWith("OneOf", StringComparison.Ordinal) => Show(v.GetType().GetProperty("Value")?.GetValue(v)),
        _ => v.ToString() ?? "null",
    };
}
