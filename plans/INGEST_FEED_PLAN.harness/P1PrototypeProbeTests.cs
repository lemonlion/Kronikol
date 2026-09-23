using System.Net;
using System.Text;
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

// TEMPORARY PROBE for plans/INGEST_FEED_PLAN.md (prototype worktree only). Runs alone with
// --filter FullyQualifiedName~P1PrototypeProbeTests and writes %TEMP%\p1proto.txt.
[Collection("DiagramsFetcher")]
public class P1PrototypeProbeTests
{
    private static readonly string Out = Path.Combine(Path.GetTempPath(), "p1proto.txt");
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "0af7651916cd43dd8448eb211c80319c";

    [Fact]
    public void Probe()
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);
        try { Run(L); }
        catch (Exception ex) { L("PROBE THREW: " + ex); }
        finally { File.WriteAllText(Out, sb.ToString()); }
    }

    private static void Run(Action<string> L)
    {
        var root = Directory.CreateTempSubdirectory("kronikol-p1proto-").FullName;
        L("root: " + root);

        // ---------- 1. HEAD behaviour of a tests-file step and assertion on ingest (F7 candidate)
        L("## 1. Tests-file step + assertion on ingest: MarkerKind, annotations, stepPath");
        {
            var (req, resp) = InteractionRecord.Pair(TestId, "Probe", "POST", "http://localhost:8081/sidekick", "graphql", "web",
                requestContent: "{}", responseContent: "{\"data\":{}}", statusCode: "200",
                requestTimestamp: T0.AddMilliseconds(2000), responseTimestamp: T0.AddMilliseconds(2050));
            var capture = Path.Combine(root, "f7.ndjson");
            File.WriteAllLines(capture, [req.ToJson(), resp.ToJson()]);
            var dir = Path.Combine(root, "f7");
            var result = IngestPipeline.Run(new IngestRequest
            {
                InteractionFiles = [capture],
                TestRecords =
                [
                    new TestRunRecord { Event = "start", TestId = TestId, TestName = "Probe", Feature = "probe.feature", Timestamp = T0 },
                    new TestRunRecord { Event = "step", TestId = TestId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 1500, Timestamp = T0.AddMilliseconds(1000) },
                    new TestRunRecord { Event = "assertion", TestId = TestId, Text = "the basket is empty", Status = "passed", Timestamp = T0.AddMilliseconds(3000) },
                    new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
                ],
                Options = Options(dir),
                CallTreeOrdering = false,
            });
            L($"Replayed={result.InteractionCount} scenarios={result.ScenarioCount}");
            L("diagnostics: " + string.Join(" | ", result.Diagnostics.Select(d => $"{d.Kind}: {d.Message}")));
            foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == TestId && l.IsDiagramMarker))
                L($"   marker log: start={log.IsOverrideStart} end={log.IsOverrideEnd} action={log.IsActionStart} MarkerKind={log.MarkerKind} puml={Show(log.PlantUml)}");
            var scenario = Scenario(Path.Combine(dir, "TestRunReport.json"));
            L("annotations: " + scenario.GetProperty("annotations").GetRawText());
            foreach (var i in scenario.GetProperty("httpInteractions").EnumerateArray())
                L($"   interaction {Str(i, "type")} {Str(i, "method")} {Str(i, "uri")} stepPath={Str(i, "stepPath")}");
            L("steps: " + scenario.GetProperty("steps").GetRawText());
        }

        // ---------- 2. In-process versus ingest, the T7 shape
        L("");
        L("## 2. In-process versus ingest (T7 shape), default options");
        Compare(L, root, "default", _ => { });

        L("");
        L("## 3. In-process versus ingest, SeparateSetup = true");
        Compare(L, root, "separateSetup", o => o.SeparateSetup = true);

        // ---------- 4. Steps twice: writer markers plus a tests file with the same step
        L("");
        L("## 4. Writer step marker + tests-file step event for the same scenario");
        {
            var capture = Path.Combine(root, "twice.ndjson");
            using (var writer = new NdjsonInteractionWriter(capture))
                foreach (var log in Fixture())
                    writer.Log(log);
            var dir = Path.Combine(root, "twice");
            var result = IngestPipeline.Run(new IngestRequest
            {
                InteractionFiles = [capture],
                TestRecords =
                [
                    new TestRunRecord { Event = "start", TestId = TestId, TestName = "Probe", Feature = "probe.feature", Timestamp = T0 },
                    new TestRunRecord { Event = "step", TestId = TestId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 1500, Timestamp = T0.AddMilliseconds(1000) },
                    new TestRunRecord { Event = "assertion", TestId = TestId, Text = "the basket is empty", Status = "passed", Timestamp = T0.AddMilliseconds(4500) },
                    new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = 8000, Timestamp = T0.AddMilliseconds(8000) },
                ],
                Options = Options(dir),
                CallTreeOrdering = false,
            });
            var scenario = Scenario(Path.Combine(dir, "TestRunReport.json"));
            var diagram = scenario.GetProperty("diagrams")[0].GetString() ?? "";
            L($"Replayed={result.InteractionCount}; stepDelimiter bars={Count(diagram, "<<stepDelimiter>>")}; assertion notes={Count(diagram, "<<assertionNote>>")}; interactions={scenario.GetProperty("httpInteractions").GetArrayLength()}");
            L("annotations: " + scenario.GetProperty("annotations").GetRawText());
            foreach (var i in scenario.GetProperty("httpInteractions").EnumerateArray())
                L($"   interaction {Str(i, "type")} {Str(i, "method")} {Str(i, "uri")} stepPath={Str(i, "stepPath")}");
            L("diagnostics: " + string.Join(" | ", result.Diagnostics.Select(d => $"{d.Kind}: {d.Message}")));
        }
    }

    private static void Compare(Action<string> L, string root, string label, Action<ReportConfigurationOptions> tweak)
    {
        var testRecords = new TestRunRecord[]
        {
            new() { Event = "start", TestId = TestId, TestName = "Probe", Feature = "probe.feature", Timestamp = T0 },
            new() { Event = "end", TestId = TestId, Status = "passed", DurationMs = 8000, Timestamp = T0.AddMilliseconds(8000) },
        };

        // (a) in-process: the fixture logged through the store, the way an adapter's run end sees it.
        var logs = Fixture();
        RequestResponseLogger.Clear(); // probe only: the class runs alone
        foreach (var log in logs)
            RequestResponseLogger.Log(log);
        var stored = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == TestId).ToArray();
        L($"[{label}] in-process store holds {stored.Length} logs for the test id");
        var synthesised = FeatureSynthesizer.Build(testRecords, stored, "Ingested", ExecutionResult.Passed, null);
        var inProcDir = Path.Combine(root, label + "-inproc");
        var inProcOptions = Options(inProcDir);
        tweak(inProcOptions);
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(synthesised.Features, synthesised.Start, synthesised.End, inProcOptions,
            RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
        DefaultDiagramsFetcher.Reset();
        var a = Scenario(Path.Combine(inProcDir, "TestRunReport.json"));

        // (b) the same logs written through the writer, read back by the pipeline.
        var capture = Path.Combine(root, label + ".ndjson");
        using (var writer = new NdjsonInteractionWriter(capture))
            foreach (var log in logs)
                writer.Log(log);
        L($"[{label}] lines written: {File.ReadAllLines(capture).Length}");
        foreach (var line in File.ReadAllLines(capture).Where(l => l.Contains("\"kind\":\"marker\"", StringComparison.Ordinal)).Take(4))
            L("   marker line: " + line);
        var ingestDir = Path.Combine(root, label + "-ingest");
        var ingestOptions = Options(ingestDir);
        tweak(ingestOptions);
        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords = testRecords,
            Options = ingestOptions,
            CallTreeOrdering = false,
        });
        L($"[{label}] Replayed={result.InteractionCount}; diagnostics: " + string.Join(" | ", result.Diagnostics.Select(d => $"{d.Kind}: {d.Message}")));
        var b = Scenario(Path.Combine(ingestDir, "TestRunReport.json"));

        // httpInteractions member by member
        var ia = a.GetProperty("httpInteractions").EnumerateArray().ToArray();
        var ib = b.GetProperty("httpInteractions").EnumerateArray().ToArray();
        L($"[{label}] httpInteractions: in-process {ia.Length}, ingest {ib.Length}");
        for (var i = 0; i < Math.Max(ia.Length, ib.Length); i++)
        {
            if (i >= ia.Length) { L($"   #{i}: only in ingest: {ib[i].GetRawText()}"); continue; }
            if (i >= ib.Length) { L($"   #{i}: only in-process: {ia[i].GetRawText()}"); continue; }
            var names = ia[i].EnumerateObject().Select(p => p.Name).Union(ib[i].EnumerateObject().Select(p => p.Name)).ToArray();
            var diffs = new List<string>();
            foreach (var n in names)
            {
                var va = ia[i].TryGetProperty(n, out var pa) ? pa.GetRawText() : "<absent>";
                var vb = ib[i].TryGetProperty(n, out var pb) ? pb.GetRawText() : "<absent>";
                if (va != vb) diffs.Add($"{n}: {va} vs {vb}");
            }
            L($"   #{i} {Str(ia[i], "type")} {Str(ia[i], "method")} {Str(ia[i], "uri")}: " + (diffs.Count == 0 ? "identical" : string.Join("; ", diffs)));
        }

        var annA = a.GetProperty("annotations").GetRawText();
        var annB = b.GetProperty("annotations").GetRawText();
        L($"[{label}] annotations {(annA == annB ? "identical" : "DIFFER")}: in-process {annA} | ingest {annB}");

        var dA = a.GetProperty("diagrams")[0].GetString() ?? "";
        var dB = b.GetProperty("diagrams")[0].GetString() ?? "";
        L($"[{label}] diagram source {(dA == dB ? "BYTE-IDENTICAL" : "DIFFERS")} ({dA.Length} vs {dB.Length} chars)");
        if (dA != dB)
        {
            var la = dA.Split('\n'); var lb = dB.Split('\n');
            for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                var x = i < la.Length ? la[i] : "<eof>"; var y = i < lb.Length ? lb[i] : "<eof>";
                if (x != y) L($"   line {i}: in-process {Show(x)} | ingest {Show(y)}");
            }
        }
        L($"[{label}] steps: in-process {a.GetProperty("steps").GetRawText()} | ingest {b.GetProperty("steps").GetRawText()}");
        File.WriteAllText(Path.Combine(root, label + "-inproc.puml"), dA);
        File.WriteAllText(Path.Combine(root, label + "-ingest.puml"), dB);
    }

    /// <summary>One scenario in enqueue order, strictly increasing tick-aligned timestamps, every marker kind.</summary>
    private static List<RequestResponseLog> Fixture()
    {
        var traceA = Guid.NewGuid(); var rrA = Guid.NewGuid();
        var traceB = Guid.NewGuid(); var rrB = Guid.NewGuid();
        var traceC = Guid.NewGuid(); var rrC = Guid.NewGuid();
        return
        [
            Marker(DiagramMarkerKind.Step, InteractionRecord.StepDelimiterPlantUml("Given", "a basket"), start: true, T0.AddMilliseconds(1000)),
            Marker(DiagramMarkerKind.Step, null, start: false, T0.AddMilliseconds(1001)),
            new("Probe", TestId, HttpMethod.Post, "{}", new Uri("http://localhost:8081/sidekick"), [("Content-Type", "application/json")], "graphql", "web", RequestResponseType.Request, traceA, rrA, false) { Timestamp = T0.AddMilliseconds(2000), Phase = TestPhase.Setup },
            new("Probe", TestId, HttpMethod.Post, "{\"data\":{}}", new Uri("http://localhost:8081/sidekick"), [], "graphql", "web", RequestResponseType.Response, traceA, rrA, false, HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(2050), DurationMs = 77, Phase = TestPhase.Setup },
            Marker(DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3", start: true, T0.AddMilliseconds(3000)),
            Marker(DiagramMarkerKind.Row, null, start: false, T0.AddMilliseconds(3001)),
            new("Probe", TestId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = T0.AddMilliseconds(3500) },
            Marker(DiagramMarkerKind.Custom, "note over graphql : cache warmed", start: true, T0.AddMilliseconds(4000)),
            Marker(DiagramMarkerKind.Custom, null, start: false, T0.AddMilliseconds(4001)),
            Marker(DiagramMarkerKind.Assertion, InteractionRecord.AssertionNotePlantUml("the basket is empty", true, null), start: true, T0.AddMilliseconds(4500)),
            Marker(DiagramMarkerKind.Assertion, null, start: false, T0.AddMilliseconds(4501)),
            new("Probe", TestId, HttpMethod.Get, null, new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Request, traceB, rrB, false) { Timestamp = T0.AddMilliseconds(5000), DurationMs = 5, Phase = TestPhase.Action },
            new("Probe", TestId, HttpMethod.Get, "{\"ok\":true}", new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Response, traceB, rrB, false, HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(5005), DurationMs = 5, Phase = TestPhase.Action },
            new("Probe", TestId, HttpMethod.Post, "{\"amount\":1}", new Uri("http://localhost:8081/pay"), [], "payments", "web", RequestResponseType.Request, traceC, rrC, false) { Timestamp = T0.AddMilliseconds(6000), Phase = TestPhase.Action },
            new("Probe", TestId, HttpMethod.Post, null, new Uri("http://localhost:8081/pay"), [], "payments", "web", RequestResponseType.Response, traceC, rrC, false) { Timestamp = T0.AddMilliseconds(6100), Error = "HttpRequestException: boom", Phase = TestPhase.Action },
        ];
    }

    private static RequestResponseLog Marker(DiagramMarkerKind kind, string? plantUml, bool start, DateTimeOffset at) =>
        new("Probe", TestId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = start, IsOverrideEnd = !start, MarkerKind = kind,
            PlantUml = plantUml is null ? null : $"\n{plantUml}\n\n", Timestamp = at,
        };

    private static ReportConfigurationOptions Options(string dir)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = dir;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return options;
    }

    private static JsonElement Scenario(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].Clone();
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ToString() : "<absent>";

    private static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string Show(string? s) => s is null ? "null" : "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
