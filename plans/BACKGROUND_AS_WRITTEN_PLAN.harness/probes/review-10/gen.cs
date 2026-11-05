using Kronikol.Reports;
using Kronikol;

// usage: gen <out-folder>; writes one report per shape under it.
var outRoot = args[0];

Scenario Row(string id, string row, ExecutionResult result, ScenarioStep[]? background, ScenarioStep[]? steps, string outline = "Pay", string? error = null) => new()
{
    Id = id,
    DisplayName = outline,
    OutlineId = outline,
    Result = result,
    ExampleValues = new Dictionary<string, string> { ["row"] = row },
    ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
    BackgroundSteps = background,
    Steps = steps,
    ErrorMessage = result == ExecutionResult.Failed ? (error ?? "it failed") : null,
};
ScenarioStep Leaf(string text, ExecutionResult status = ExecutionResult.Passed, string kw = "Given") => new() { Keyword = kw, Text = text, Status = status };
ScenarioStep Comp(string text, ExecutionResult status, params ScenarioStep[] subs) => new() { Keyword = "When", Text = text, Status = status, SubSteps = subs };
ScenarioStep[] Bg(string text = "a customer is signed in", ExecutionResult status = ExecutionResult.Passed) => [Leaf(text, status)];
// composite 0 holds a composite 0.0 (undrawn until 0 opens) and a leaf; then a plain step
ScenarioStep[] Nested(string? word = null) =>
[
    Comp("the customer pays", ExecutionResult.Passed,
        Comp("the card is read", ExecutionResult.Passed, Leaf("the chip answers"), Leaf("the pin is checked")),
        Leaf("the till prints")),
    Leaf(word is null ? "the order ships" : $"the {word} order ships", ExecutionResult.Passed, "Then"),
];
void Report(string name, Scenario[] rows, bool separate = true, Action<ReportConfigurationOptions>? tune = null, Feature[]? features = null)
{
    var options = new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(outRoot, name),
        SeparateBackgroundSteps = separate,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
    };
    tune?.Invoke(options);
    ReportGenerator.CreateStandardReportsWithDiagrams(features ?? [new Feature { DisplayName = "Orders", Scenarios = rows }], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    Console.WriteLine(name + " done");
}

// b holds zebra in its Background, d holds stripe; a, c, e neither. Every row has the nested composite.
Report("nested",
[
    Row("n1", "a", ExecutionResult.Passed, Bg(), Nested()),
    Row("n2", "b", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Nested()),
    Row("n3", "c", ExecutionResult.Passed, Bg(), Nested()),
    Row("n4", "d", ExecutionResult.Passed, Bg("a stripe customer is signed in"), Nested()),
    Row("n5", "e", ExecutionResult.Passed, Bg(), Nested()),
]);

// the word of the first row, and a word in several rows
Report("first",
[
    Row("f1", "a", ExecutionResult.Passed, Bg("a kiwi customer is signed in"), Nested()),
    Row("f2", "b", ExecutionResult.Passed, Bg(), Nested()),
    Row("f3", "c", ExecutionResult.Passed, Bg("a fig customer is signed in"), Nested()),
    Row("f4", "d", ExecutionResult.Passed, Bg(), Nested()),
]);

// a passes; b fails in its Background; c never starts; d fails in its Steps; e passes; f fails deep in a composite; g passes with lynx; h never starts, its error holds okapi
Report("fail",
[
    Row("x1", "a", ExecutionResult.Passed, Bg(), Nested()),
    Row("x2", "b", ExecutionResult.Failed, Bg("a customer is signed in", ExecutionResult.Failed), null),
    Row("x3", "c", ExecutionResult.Failed, null, null),
    Row("x4", "d", ExecutionResult.Failed, Bg(), [Leaf("the customer pays", ExecutionResult.Failed, "When")]),
    Row("x5", "e", ExecutionResult.Passed, Bg(), Nested()),
    Row("x6", "f", ExecutionResult.Failed, Bg(),
    [
        Comp("the customer pays", ExecutionResult.Passed,
            Comp("the card is read", ExecutionResult.Passed, Leaf("the chip answers", ExecutionResult.Failed)),
            Leaf("the till prints")),
        Leaf("the order ships", ExecutionResult.Skipped, "Then"),
    ]),
    Row("x7", "g", ExecutionResult.Passed, Bg("a lynx customer is signed in"), Nested()),
    Row("x8", "h", ExecutionResult.Failed, null, null, error: "the okapi failed"),
]);

// two outlines; ibis is in Pay b alone, heron in Pay c and Refund b, so two scenarios match
Report("two",
[
    Row("t1", "a", ExecutionResult.Passed, Bg(), Nested()),
    Row("t2", "b", ExecutionResult.Passed, Bg("an ibis customer is signed in"), Nested()),
    Row("t3", "c", ExecutionResult.Passed, Bg("a heron customer is signed in"), Nested()),
    Row("t4", "d", ExecutionResult.Passed, Bg(), Nested()),
    Row("t5", "a", ExecutionResult.Passed, Bg(), Nested(), outline: "Refund"),
    Row("t6", "b", ExecutionResult.Passed, Bg("a heron refund is signed in"), Nested(), outline: "Refund"),
    Row("t7", "c", ExecutionResult.Passed, Bg(), Nested(), outline: "Refund"),
]);

// the layout: Background steps plain and composite, the scenario's own plain and composite, one that fails, with and without the section
ScenarioStep[] layoutBg = [Leaf("a plain background step"), Comp("a composite background step", ExecutionResult.Passed, Leaf("its sub step"))];
ScenarioStep[] layoutOwn = [Leaf("a plain own step", ExecutionResult.Passed, "When"), Comp("a composite own step", ExecutionResult.Passed, Leaf("its sub step")), Leaf("the last own step", ExecutionResult.Passed, "Then")];
Scenario[] Layout() =>
[
    new() { Id = "l1", DisplayName = "Plain scenario", Result = ExecutionResult.Passed, BackgroundSteps = layoutBg, Steps = layoutOwn },
    Row("l2", "a", ExecutionResult.Passed, layoutBg, layoutOwn),
    Row("l3", "b", ExecutionResult.Passed, layoutBg, layoutOwn),
];
Report("layout-inline", Layout(), separate: false);
Report("layout-separate", Layout(), separate: true);

// a plain scenario next to an outline, whose name is the word: lands on the outline with no row's text matching
Report("name",
[
    new() { Id = "p1", DisplayName = "Plain scenario", Result = ExecutionResult.Passed, BackgroundSteps = Bg(), Steps = Nested() },
    Row("p2", "a", ExecutionResult.Passed, Bg(), Nested(), outline: "Walrus pays"),
    Row("p3", "b", ExecutionResult.Passed, Bg(), Nested(), outline: "Walrus pays"),
    Row("p4", "c", ExecutionResult.Passed, Bg(), Nested(), outline: "Walrus pays"),
]);

// composite 0's sub-steps are long enough to be held until it first opens, 0.0 inside the same entry; a passing second composite 1 too
string Long(string w) => string.Concat(Enumerable.Repeat(w + " says yes and the terminal answers ", 70));
ScenarioStep[] Held() =>
[
    Comp("the customer pays", ExecutionResult.Passed,
        Comp("the card is read", ExecutionResult.Passed, Leaf(Long("chip")), Leaf("the pin is checked")),
        Leaf("the till prints")),
    Leaf("the order ships", ExecutionResult.Passed, "Then"),
];
Report("held",
[
    Row("h1", "a", ExecutionResult.Passed, Bg(), Held()),
    Row("h2", "b", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Held()),
    Row("h3", "c", ExecutionResult.Passed, Bg(), Held()),
    Row("h4", "d", ExecutionResult.Passed, Bg("a stripe customer is signed in"), Held()),
    Row("h5", "e", ExecutionResult.Passed, Bg(), Held()),
]);
Report("set-open",
[
    Row("o1", "a", ExecutionResult.Passed, Bg(), Nested()),
    Row("o2", "b", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Nested()),
    Row("o3", "c", ExecutionResult.Passed, Bg(), Nested()),
], tune: o => { o.TestRunReportToggleDefaults.BackgroundStepsOpen = true; o.TestRunReportToggleDefaults.StepsSectionOpen = false; });

// the first row never reached its steps, so nothing it leaves says what the Background or the Steps were
Report("lack",
[
    Row("k1", "a", ExecutionResult.Failed, null, null),
    Row("k2", "b", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Nested()),
    Row("k3", "c", ExecutionResult.Passed, Bg(), Nested()),
    Row("k4", "d", ExecutionResult.Passed, Bg(), Nested()),
]);

// real diagrams, in a section set to start closed
{
    Scenario[] rows =
    [
        Row("g1", "a", ExecutionResult.Passed, Bg(), Nested()),
        Row("g2", "b", ExecutionResult.Passed, Bg(), Nested()),
        Row("g3", "c", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Nested()),
    ];
    var folder = Path.Combine(outRoot, "diag");
    Directory.CreateDirectory(folder);
    const string source = """
        @startuml
        actor "Caller" as caller
        participant "Service" as svc
        caller -> svc : GET /api/test
        svc --> caller : 200 OK
        @enduml
        """;
    var diagrams = rows.Select(r => new DefaultDiagramsFetcher.DiagramAsCode(r.Id, "", source.Replace("/api/test", "/api/" + r.Id))).ToArray();
    ReportGenerator.GenerateHtmlReport(diagrams, [new Feature { DisplayName = "Orders", Scenarios = rows }], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, null,
        Path.Combine(folder, "TestRunReport.html"), "Diagrams", true, separateBackgroundSteps: true,
        toggleDefaults: new ResolvedToggleDefaults { DiagramsSectionOpen = false });
    Console.WriteLine("diag done");
}
