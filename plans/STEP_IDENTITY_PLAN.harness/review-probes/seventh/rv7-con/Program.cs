using System.Net;
using System.Reflection;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

// usage: Con all            every shape, bars on and off, read through the walk itself
//        Con <shape> <on|off> <reportDir>   one shape, a real report
var attribute = typeof(ReportGenerator).GetMethod("AttributeInteractionsToStepPaths", BindingFlags.NonPublic | BindingFlags.Static)!;
var shapes = Shapes.All;

if (args[0] == "all")
{
    foreach (var (name, build) in shapes)
        foreach (var bars in new[] { true, false })
        {
            StepCollector.Options.ShowStepDelimiters = bars;
            var id = name + (bars ? "#on" : "#off");
            var scenario = build(id);
            var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToArray();
            var collector = new ReportDiagnosticsCollector();
            Dictionary<string, List<string?>> read;
            using (ReportDiagnosticsScope.Begin(collector))
                read = (Dictionary<string, List<string?>>)attribute.Invoke(null, [logs, new[] { new Feature { DisplayName = "F", Scenarios = [scenario] } }])!;
            var calls = logs.Where(l => !l.IsDiagramMarker).ToArray();
            var parts = new List<string>();
            var paths = read.TryGetValue(id, out var p) ? p : [];
            for (var i = 0; i < calls.Length; i++)
                if (calls[i].Type == RequestResponseType.Request)
                    parts.Add($"{calls[i].Uri.AbsolutePath}={paths[i] ?? "-"}");
            var mismatches = collector.Entries.Count(e => e.Kind == DiagnosticKind.StepAttributionMismatch);
            Console.WriteLine($"{name,-34} {(bars ? "on " : "off")}  {string.Join(" ", parts)}  mismatch={mismatches}");
        }
    return;
}

{
    var (name, build) = shapes.First(s => s.Name == args[0]);
    StepCollector.Options.ShowStepDelimiters = args[1] == "on";
    var dir = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(dir);
    var start = DateTime.UtcNow;
    var scenario = build(name);
    ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = name, Scenarios = [scenario] }], start, DateTime.UtcNow, new ReportConfigurationOptions
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
    });
}

static class Shapes
{
    static void Call(string testId, string path) =>
        RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);

    static void Step(string id, string? keyword, string text, string path)
    {
        StepCollector.StartStep(id, keyword, text, null, null);
        Call(id, path);
        StepCollector.CompleteStep(id, true);
    }

    static void Bar(string id, string label) =>
        DefaultTrackingDiagramOverride.InsertPlantUml(id, "hnote across <<stepDelimiter>> #black:<color:white>" + label, DiagramMarkerKind.Step);

    static ScenarioStep Rebuilt(ScenarioStep c) => new() { Keyword = c.Keyword, Text = c.Text, Status = c.Status, SubSteps = c.SubSteps, Duration = c.Duration };
    static ScenarioStep Own(string? keyword, string text) => new() { Keyword = keyword, Text = text, Status = ExecutionResult.Passed };
    static Scenario S(string id, ScenarioStep[]? steps, ScenarioStep[]? background = null) =>
        new() { Id = id, DisplayName = id, Result = ExecutionResult.Passed, Steps = steps, BackgroundSteps = background };

    public static readonly (string Name, Func<string, Scenario> Build)[] All =
    [
        ("control", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            return S(id, StepCollector.GetSteps(id));
        }),

        // ---- a stamp with no row
        ("open-at-report", id =>
        {
            Step(id, "Given", "a thing", "given");
            StepCollector.StartStep(id, "When", "a step that never ends", null, null);
            Call(id, "open");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("open-then-nested", id =>
        {
            Step(id, "Given", "a thing", "given");
            StepCollector.StartStep(id, "When", "a step that never ends", null, null);
            Step(id, null, "a step inside it", "nested");
            Call(id, "after-nested");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("open-text-of-next-row", id =>
        {
            // A consumer's row after the collected ones, and a step still open whose bar holds that row's text
            Step(id, "Given", "a thing", "given");
            StepCollector.StartStep(id, "When", "the next row and more", null, null);
            Call(id, "open");
            return S(id, [.. StepCollector.GetSteps(id), Own("When", "the next row")]);
        }),
        ("dropped-row", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "a dropped thing", "dropped");
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], c[2]]);
        }),
        ("dropped-row-text-inside", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "the order is placed and paid", "dropped");
            Step(id, "Then", "the order is placed", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], c[2]]);
        }),
        ("dropped-first-row", id =>
        {
            Step(id, "Given", "a dropped thing", "dropped");
            Step(id, "When", "another thing", "b");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[1]]);
        }),
        ("dropped-sub", id =>
        {
            StepCollector.StartStep(id, "Given", "a parent", null, null);
            Call(id, "parent");
            Step(id, null, "a sub-step", "sub");
            Call(id, "parent-after");
            StepCollector.CompleteStep(id, true);
            Step(id, "When", "another thing", "next");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0] with { SubSteps = null }, c[1]]);
        }),

        // ---- a consumer's rows
        ("mix-new-row", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            Step(id, "Then", "the last thing", "then");
            return S(id, StepCollector.GetSteps(id).Select(c => c.Keyword == "When" ? Rebuilt(c) with { Comments = ["annotated"] } : c).ToArray());
        }),
        ("mix-new-row-renamed", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            Step(id, "Then", "the last thing", "then");
            return S(id, StepCollector.GetSteps(id).Select(c => c.Keyword == "When" ? Rebuilt(c) with { Text = "renamed" } : c).ToArray());
        }),
        ("mix-first-row-rebuilt", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            var c = StepCollector.GetSteps(id);
            return S(id, [Rebuilt(c[0]), c[1]]);
        }),
        ("mix-last-row-rebuilt", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], Rebuilt(c[1])]);
        }),
        ("mix-sub-under-rebuilt", id =>
        {
            Step(id, "Given", "a thing", "given");
            StepCollector.StartStep(id, "When", "a parent", null, null);
            Call(id, "parent");
            Step(id, null, "a sub-step", "sub");
            Call(id, "parent-after");
            StepCollector.CompleteStep(id, true);
            Step(id, "Then", "the last thing", "then");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], Rebuilt(c[1]), c[2]]);
        }),
        ("mix-background", id =>
        {
            Step(id, "Given", "a shop", "shop");
            Step(id, "When", "a basket", "basket");
            Step(id, "Then", "it is empty", "check");
            var c = StepCollector.GetSteps(id);
            return S(id, c[1..], [Rebuilt(c[0])]);
        }),
        ("mix-background-after-hook", id =>
        {
            // A hook's step first, and the background rebuilt by hand: the rows are not in the order the bars come
            Step(id, null, "a hook", "hook");
            Step(id, "Given", "a shop", "shop");
            Step(id, "When", "a basket", "basket");
            Step(id, "Then", "it is empty", "check");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], c[2], c[3]], [Rebuilt(c[1])]);
        }),
        ("all-rebuilt", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            return S(id, StepCollector.GetSteps(id).Select(Rebuilt).ToArray());
        }),
        ("all-rebuilt-over-subs", id =>
        {
            StepCollector.StartStep(id, "Given", "a parent", null, null);
            Step(id, null, "a sub-step", "sub");
            StepCollector.CompleteStep(id, true);
            Step(id, "When", "another thing", "when");
            return S(id, StepCollector.GetSteps(id).Select(Rebuilt).ToArray());
        }),
        ("with-copy", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            return S(id, StepCollector.GetSteps(id).Select(c => c with { Text = c.Text + " (renamed)" }).ToArray());
        }),
        ("with-copy-twice", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], c[0] with { Text = "the copy" }, c[1]]);
        }),
        ("same-row-in-background-and-steps", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            var c = StepCollector.GetSteps(id);
            return S(id, c, [c[0]]);
        }),
        ("prepend-hand-row", id =>
        {
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            return S(id, [Own("Given", "the adapter's own row"), .. StepCollector.GetSteps(id)]);
        }),
        ("prepend-hand-row-with-text", id =>
        {
            // the adapter's row in front holds the first step's text: no bar is its
            Step(id, "Given", "a thing", "given");
            Step(id, "When", "another thing", "when");
            return S(id, [Own("Given", "a thing"), .. StepCollector.GetSteps(id)]);
        }),

        // ---- a row not reached in order
        ("rows-reversed", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "another thing", "b");
            Step(id, "Then", "the last thing", "c");
            return S(id, StepCollector.GetSteps(id).Reverse().ToArray());
        }),
        ("rows-reversed-one-rebuilt", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "another thing", "b");
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[2], Rebuilt(c[1]), c[0]]);
        }),
        ("rebuilt-sorted-first", id =>
        {
            // rows in an order of the adapter's: the second bar is a rebuilt row the place has passed
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "another thing", "b");
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [Rebuilt(c[1]), c[0], c[2]]);
        }),
        ("own-row-bar-after-collected", id =>
        {
            // rows [own1, c0, own2], the bars c0, own1, own2
            Step(id, "When", "a collected thing", "c0");
            Bar(id, "Given own one"); Call(id, "own1");
            Bar(id, "Then own two"); Call(id, "own2");
            return S(id, [Own("Given", "own one"), .. StepCollector.GetSteps(id), Own("Then", "own two")]);
        }),
        ("own-rows-bars-in-order", id =>
        {
            Bar(id, "Given own one"); Call(id, "own1");
            Step(id, "When", "a collected thing", "c0");
            Bar(id, "Then own two"); Call(id, "own2");
            return S(id, [Own("Given", "own one"), .. StepCollector.GetSteps(id), Own("Then", "own two")]);
        }),
        ("bar-unstamped-mid", id =>
        {
            Step(id, "Given", "a thing", "a");
            Bar(id, "Cleanup"); Call(id, "cleanup");
            Step(id, "When", "another thing", "b");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("bar-unstamped-mid-text", id =>
        {
            Step(id, "Given", "a thing", "a");
            Bar(id, "Before another thing"); Call(id, "manual");
            Step(id, "When", "another thing", "b");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("bar-inside-a-step", id =>
        {
            StepCollector.StartStep(id, "Given", "a thing", null, null);
            Call(id, "a");
            Bar(id, "Part two"); Call(id, "part2");
            StepCollector.CompleteStep(id, true);
            Step(id, "When", "another thing", "b");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("bar-inside-a-rebuilt-step", id =>
        {
            Step(id, "Given", "a thing", "a");
            StepCollector.StartStep(id, "When", "another thing", null, null);
            Call(id, "b");
            Bar(id, "Part two"); Call(id, "part2");
            StepCollector.CompleteStep(id, true);
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], Rebuilt(c[1]), c[2]]);
        }),
        ("empty-text-row-at-place", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "a dropped step", "dropped");
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], Own("When", ""), c[2]]);
        }),
        ("after-the-steps", id =>
        {
            Step(id, "Given", "a thing", "a");
            Call(id, "between");
            Step(id, "When", "another thing", "b");
            Call(id, "after-last");
            return S(id, StepCollector.GetSteps(id));
        }),
        ("before-the-steps", id =>
        {
            Call(id, "before");
            Step(id, "Given", "a thing", "a");
            return S(id, StepCollector.GetSteps(id));
        }),

        ("named-bar-moves-place-back", id =>
        {
            // rows [hook, r0, r1], the bars r0, hook, r1: the hook's bar sets the place back
            Step(id, "Given", "own row one", "r0");
            Step(id, null, "a hook", "hook");
            Step(id, "When", "own row two", "r1");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[1], Rebuilt(c[0]), Rebuilt(c[2])]);
        }),
        ("hook-listed-last-rebuilt-rows-first", id =>
        {
            // rows [r0, r1, hook], the bars hook, r0, r1: the hook's bar sets the place past both
            Step(id, null, "a hook", "hook");
            Step(id, "Given", "own row one", "r0");
            Step(id, "When", "own row two", "r1");
            var c = StepCollector.GetSteps(id);
            return S(id, [Rebuilt(c[1]), Rebuilt(c[2]), c[0]]);
        }),
        ("rebuilt-long-label", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "a basket that held two apples and a pear last week but nothing at all this week while the shop prices rose again today", "long");
            Step(id, "Then", "the last thing", "c");
            var c = StepCollector.GetSteps(id);
            return S(id, [c[0], Rebuilt(c[1]), c[2]]);
        }),
        ("background-with-ids-rebuilt-steps", id =>
        {
            Step(id, "Given", "a shop", "shop");
            Step(id, "When", "a basket", "basket");
            Step(id, "Then", "it is empty", "check");
            var c = StepCollector.GetSteps(id);
            return S(id, [Rebuilt(c[1]), Rebuilt(c[2])], [c[0]]);
        }),
        ("all-rows-empty-text", id =>
        {
            Step(id, "Given", "a thing", "a");
            Step(id, "When", "another thing", "b");
            var c = StepCollector.GetSteps(id);
            return S(id, [Own("Given", ""), c[1]]);
        }),

        // ---- two runs under one id, the steps cleared in between
        ("cleared-then-run-again", id =>
        {
            Step(id, "Given", "a thing", "a1");
            Step(id, "When", "another thing", "b1");
            StepCollector.ClearSteps(id);
            Step(id, "Given", "a thing", "a2");
            Step(id, "When", "another thing", "b2");
            return S(id, StepCollector.GetSteps(id));
        }),
    ];
}
