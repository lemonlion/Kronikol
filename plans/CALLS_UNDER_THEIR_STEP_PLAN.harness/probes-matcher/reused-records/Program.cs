// The walk as an in-process run meets it, and the public ingest entry with records a host keeps. Compiled against the
// change's Kronikol.dll, run against whichever folder is given, so one binary compares published and the change.
// Usage: InProc <folder holding Kronikol.dll> <what>
using System.Reflection;
using System.Text;
using Kronikol;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

var dir = args[0];
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var candidate = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
};
Console.OutputEncoding = Encoding.UTF8;
Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");
Runner.Go(args[1]);

static class Runner
{
    static List<string?> Walk(string testId, ScenarioStep[] rows, ScenarioStep[] background = null)
    {
        var scenario = new Scenario { Id = testId, DisplayName = testId, Steps = rows, BackgroundSteps = background };
        var features = new[] { new Feature { DisplayName = "F", Scenarios = [scenario] } };
        var method = typeof(ReportGenerator).GetMethod("AttributeInteractionsToStepPaths", BindingFlags.Static | BindingFlags.NonPublic);
        var result = (Dictionary<string, List<string>>)method.Invoke(null, [RequestResponseLogger.RequestAndResponseLogs, features]);
        return result.TryGetValue(testId, out var list) ? list.Cast<string?>().ToList() : [];
    }

    static void Call(string testId, string name, string rid = null, bool response = true)
    {
        rid ??= testId + name + Guid.NewGuid();
        RequestResponseLogger.Log(new InteractionRecord { Type = "Request", Method = "GET", Uri = "http://x.test/" + name, ServiceName = "S", CallerName = "T", TestId = testId, RequestResponseId = rid }.ToLog());
        if (response)
            RequestResponseLogger.Log(new InteractionRecord { Type = "Response", Method = "GET", Uri = "http://x.test/" + name, ServiceName = "S", CallerName = "T", TestId = testId, RequestResponseId = rid, StatusCode = "200" }.ToLog());
    }

    static void Show(string name, IEnumerable<string?> paths) =>
        Console.WriteLine($"{name,-46} {string.Join(" ", paths.Select(p => p ?? "-"))}");

    static string Id(string name) { var id = name + "-" + Guid.NewGuid().ToString("N")[..6]; StepCollector.ClearSteps(id); return id; }

    public static void Go(string what)
    {
        switch (what)
        {
            case "walk": WalkCases(); break;
            case "reuse": Reuse(); break;
            case "more": More(); break;
            default: Console.WriteLine("what?"); break;
        }
    }

    static void WalkCases()
    {
        var w = string.Join(" ", Enumerable.Range(0, 30).Select(i => "word" + i));

        // 1. Rows from GetSteps, with text the bar escapes and wraps, a repeated keyword, a doc string and a table.
        var t = Id("collector");
        StepCollector.StartStep(t, "Given", "a <$basket> in " + w, null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "Given", "another <$basket>", null, null); Call(t, "b"); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "When", "it is paid <$x", null, null, [["a", "b"], ["1", "2"]], "{\n \"a\": 1\n}"); Call(t, "c"); StepCollector.CompleteStep(t, true);
        Show("rows from GetSteps, 6 calls", Walk(t, StepCollector.GetSteps(t)));
        Console.WriteLine("   keywords: " + string.Join(",", StepCollector.GetSteps(t).Select(s => s.Keyword)));

        // 2. The same bars and hand-built rows with the Gherkin keyword each line was written with: a bar writes And.
        t = Id("hand-gherkin");
        StepCollector.StartStep(t, "Given", "a <$basket>", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "Given", "another <$basket>", null, null); Call(t, "b"); StepCollector.CompleteStep(t, true);
        Show("hand-built Given/Given over Given/And bars", Walk(t, [new ScenarioStep { Keyword = "Given", Text = "a <$basket>" }, new ScenarioStep { Keyword = "Given", Text = "another <$basket>" }]));

        // 3. Tracked assertions in the step list (on by default): sub-steps, off the walk.
        t = Id("assert");
        StepCollector.StartStep(t, "Given", "a basket", null, null); StepCollector.AddAssertionSubStep(t, "x is y", true); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "When", "it is paid", null, null); Call(t, "b"); StepCollector.CompleteStep(t, true);
        Show("assertion sub-step in a row", Walk(t, StepCollector.GetSteps(t)));

        // 4. Nested steps: one bar for the top one.
        t = Id("nested");
        StepCollector.StartStep(t, "Given", "outer <$x", null, null); StepCollector.StartStep(t, "When", "inner", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "Then", "last", null, null); Call(t, "b"); StepCollector.CompleteStep(t, true);
        Show("nested, one bar", Walk(t, StepCollector.GetSteps(t)));

        // 5. A bypassed step and a failed step.
        t = Id("bypass");
        StepCollector.StartStep(t, "Given", "skipped <$x", null, null); StepCollector.BypassStep(t, "because");
        StepCollector.StartStep(t, "When", "fails <$y", null, null); Call(t, "a"); StepCollector.CompleteStep(t, false, "bang");
        Show("bypassed then failed", Walk(t, StepCollector.GetSteps(t)));

        // 6. A hand-built row marked as an assertion whose step drew a bar.
        t = Id("assert-row-with-bar");
        StepCollector.StartStep(t, "Then", "it holds", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "Then", "more", null, null); Call(t, "b"); StepCollector.CompleteStep(t, true);
        Show("hand-built IsAssertion row with a bar", Walk(t, [new ScenarioStep { Keyword = "Then", Text = "it holds", IsAssertion = true }, new ScenarioStep { Keyword = "Then", Text = "more" }]));

        // 7. No-text row, hand-built, no bar for it, in-process: a bar's calls land on it.
        t = Id("no-text-hand");
        StepCollector.StartStep(t, "Given", "a basket", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        Show("hand-built (step) row, no bar for it", Walk(t, [new ScenarioStep { Text = "(step)" }, new ScenarioStep { Keyword = "Given", Text = "a basket" }]));

        // 8. An author's own step named "(step)" with a bar, in-process, then an out-of-line bar.
        t = Id("no-text-author");
        StepCollector.StartStep(t, "Given", "other", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        Show("(step) row against another row's bar", Walk(t, [new ScenarioStep { Keyword = "Given", Text = "(step)" }]));

        // 9. A call in flight across a bar, and one logged on another thread after the next bar.
        t = Id("in-flight");
        StepCollector.StartStep(t, "Given", "a basket", null, null);
        var rid = "slow-" + t;
        RequestResponseLogger.Log(new InteractionRecord { Type = "Request", Method = "GET", Uri = "http://x.test/slow", ServiceName = "S", CallerName = "T", TestId = t, RequestResponseId = rid }.ToLog());
        StepCollector.CompleteStep(t, true);
        StepCollector.StartStep(t, "When", "it is paid", null, null);
        RequestResponseLogger.Log(new InteractionRecord { Type = "Response", Method = "GET", Uri = "http://x.test/slow", ServiceName = "S", CallerName = "T", TestId = t, RequestResponseId = rid, StatusCode = "200" }.ToLog());
        Call(t, "pay");
        StepCollector.CompleteStep(t, true);
        Show("in flight: req, res, req, res", Walk(t, StepCollector.GetSteps(t)));

        // 10. A request before the first bar, its response after: the step published gave the response is gone.
        t = Id("before-first-bar");
        rid = "early-" + t;
        RequestResponseLogger.Log(new InteractionRecord { Type = "Request", Method = "GET", Uri = "http://x.test/early", ServiceName = "S", CallerName = "T", TestId = t, RequestResponseId = rid }.ToLog());
        StepCollector.StartStep(t, "Given", "a basket", null, null);
        RequestResponseLogger.Log(new InteractionRecord { Type = "Response", Method = "GET", Uri = "http://x.test/early", ServiceName = "S", CallerName = "T", TestId = t, RequestResponseId = rid, StatusCode = "200" }.ToLog());
        StepCollector.CompleteStep(t, true);
        Show("request before the first bar", Walk(t, StepCollector.GetSteps(t)));

        // 11. PrependKeyword off: the bar holds the capitalised text alone.
        var saved = StepCollector.Options;
        t = Id("no-prepend");
        StepCollector.Options = new StepTrackingOptions { PrependKeyword = false };
        StepCollector.StartStep(t, "Given", "a <$basket>", null, null); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.Options = saved;
        Show("PrependKeyword off", Walk(t, StepCollector.GetSteps(t)));

        // 12. Assertions left out of the list.
        t = Id("no-assert-list");
        StepCollector.Options = new StepTrackingOptions { IncludeTrackedAssertionsInStepList = false };
        StepCollector.StartStep(t, "Given", "a basket", null, null); StepCollector.AddAssertionSubStep(t, "x", true); Call(t, "a"); StepCollector.CompleteStep(t, true);
        StepCollector.Options = saved;
        Show("IncludeTrackedAssertionsInStepList off", Walk(t, StepCollector.GetSteps(t)));
    }

    // Records a host keeps and hands in twice: the first capture has no bars of its own, the second does.
    static void Reuse()
    {
        var t0 = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        TestRunRecord R(string ev, string text = null, double? ms = null, string kw = null, bool? bg = null) => new()
        {
            Event = ev, TestId = "s", TestName = "t", Text = text, Keyword = kw, Background = bg, Status = "passed",
            Timestamp = ms is { } m ? t0.AddMilliseconds(m) : null,
        };
        var records = new List<TestRunRecord> { R("start", ms: 0), R("step", "a customer", null, "Given"), R("step", "a basket", 100, "Given"), R("step", "it is paid", 200, "When"), R("end", ms: 500) };

        InteractionRecord Bar(string label, double ms, string id, bool end) => new()
        {
            Kind = "marker", MarkerKind = "Step", Type = "Request", Uri = "http://override.com/", ServiceName = "", CallerName = "", TestId = "s", RequestResponseId = id, Timestamp = t0.AddMilliseconds(ms),
            PlantUml = end ? null : "\nhnote across <<stepDelimiter>> #black:<color:white>" + label + "\n\n", MarkerEnd = end ? true : null,
        };
        InteractionRecord Half(string type, string path, double ms) => new()
        {
            Type = type, Method = "GET", Uri = "http://x.test/" + path, ServiceName = "S", CallerName = "T", TestId = "s", RequestResponseId = path, Timestamp = t0.AddMilliseconds(ms), StatusCode = type == "Response" ? "200" : null,
        };
        var calls = new List<InteractionRecord> { Half("Request", "basket", 110), Half("Response", "basket", 115), Half("Request", "pay", 210), Half("Response", "pay", 215) };
        var ownBars = new List<InteractionRecord> { Bar("Given a customer", 50, "b0", false), Bar("", 50, "b0e", true), Bar("Given a basket", 100, "b1", false), Bar("", 100, "b1e", true), Bar("When it is paid", 200, "b2", false), Bar("", 200, "b2e", true) };
        ownBars.AddRange(calls);
        ownBars.Add(Half("Request", "first", 60)); ownBars.Add(Half("Response", "first", 65));

        void Once(string name, List<InteractionRecord> capture, IEnumerable<TestRunRecord> tests, string folder)
        {
            var result = IngestPipeline.Run(new IngestRequest { Interactions = capture, TestRecords = tests, Options = OptionsFor(folder) });
            Console.WriteLine($"{name}: {result.Features.Length} feature, report in {folder}");
        }

        var root = Path.Combine(Environment.GetEnvironmentVariable("REUSE_OUT") ?? "out-reuse", "reuse-" + typeof(ReportGenerator).Assembly.GetName().Version + "-" + (typeof(ScenarioStep).GetProperty("DrawsNoBar", BindingFlags.Instance | BindingFlags.NonPublic) is null ? "pub" : "new"));
        Once("fresh records, capture draws", ownBars, records.Select(r => r with { }).ToList(), Path.Combine(root, "fresh"));
        // the same objects: first over a capture with no bars, then over the one with bars
        Once("same objects, first capture has no bars", calls, records, Path.Combine(root, "first"));
        Once("same objects, second capture draws", ownBars, records, Path.Combine(root, "second"));
    }

    // Assertions off the list on an ingest, and two shards through the mergeable file.
    static void More()
    {
        var t0 = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var label = typeof(ScenarioStep).GetProperty("DrawsNoBar", BindingFlags.Instance | BindingFlags.NonPublic) is null ? "pub" : "new";
        List<TestRunRecord> Tests(string id, double at) =>
        [
            new() { Event = "start", TestId = id, TestName = id, Feature = "F", Timestamp = t0.AddMilliseconds(at) },
            new() { Event = "assertion", TestId = id, Text = "the seed is there", Status = "passed", Timestamp = t0.AddMilliseconds(at + 50) },
            new() { Event = "step", TestId = id, Text = "a customer", Keyword = "Given", Background = true, Status = "passed", Timestamp = t0.AddMilliseconds(at + 60) },
            new() { Event = "step", TestId = id, Text = "a <$basket>", Keyword = "Given", Status = "passed", Timestamp = t0.AddMilliseconds(at + 100) },
            new() { Event = "step", TestId = id, Text = "it is paid", Keyword = "When", Status = "passed", Timestamp = t0.AddMilliseconds(at + 200) },
            new() { Event = "end", TestId = id, Status = "passed", Timestamp = t0.AddMilliseconds(at + 500) },
        ];
        InteractionRecord Half(string id, string type, string path, double ms) => new()
        {
            Type = type, Method = "GET", Uri = "http://x.test/" + path, ServiceName = "S", CallerName = "T", TestId = id, RequestResponseId = id + path, Timestamp = t0.AddMilliseconds(ms), StatusCode = type == "Response" ? "200" : null,
        };
        List<InteractionRecord> Calls(string id, double at) => [Half(id, "Request", "basket", at + 110), Half(id, "Response", "basket", at + 115), Half(id, "Request", "pay", at + 210), Half(id, "Response", "pay", at + 215)];

        var root = (Environment.GetEnvironmentVariable("REUSE_OUT") ?? "out-reuse") + "/more-" + label;
        // On an ingest with the assertion left off the list.
        var saved = StepCollector.Options;
        StepCollector.Options = new StepTrackingOptions { IncludeTrackedAssertionsInStepList = false };
        IngestPipeline.Run(new IngestRequest { Interactions = Calls("a", 0), TestRecords = Tests("a", 0), Options = OptionsFor(root + "/noassert") });
        StepCollector.Options = saved;
        // Two shards with the mergeable data on.
        foreach (var (id, at) in new[] { ("a", 0.0), ("b", 1000.0) })
            IngestPipeline.Run(new IngestRequest { Interactions = Calls(id, at), TestRecords = Tests(id, at), Options = OptionsFor(root + "/shard-" + id) with { GenerateMergeableData = true } });
    }

    static ReportConfigurationOptions OptionsFor(string folder) =>
        IngestPipeline.DefaultOptions() with { GenerateComponentDiagram = false, ReportsFolderPath = folder };
}
