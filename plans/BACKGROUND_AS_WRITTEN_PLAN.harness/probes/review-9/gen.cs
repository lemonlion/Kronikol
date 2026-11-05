using Kronikol;
using Kronikol.Reports;

var outRoot = args[0];

static ScenarioStep P(string kw, string text, ExecutionResult st = ExecutionResult.Passed) => new() { Keyword = kw, Text = text, Status = st };
static ScenarioStep C(string kw, string text, ExecutionResult st = ExecutionResult.Passed, params ScenarioStep[] subs) =>
    new() { Keyword = kw, Text = text, Status = st, SubSteps = subs.Length > 0 ? subs : [P("Given", text + " inside")] };

static Scenario Row(string id, string row, ExecutionResult r, ScenarioStep[]? bg, ScenarioStep[]? st, string outline = "Pay", string? err = null) => new()
{
    Id = id,
    DisplayName = outline,
    OutlineId = outline,
    Result = r,
    ExampleValues = new Dictionary<string, string> { ["row"] = row },
    ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
    BackgroundSteps = bg,
    Steps = st,
    ErrorMessage = r == ExecutionResult.Failed ? (err ?? "it failed") : null,
};
static Scenario Plain(string id, string name, ExecutionResult r, ScenarioStep[]? bg, ScenarioStep[]? st) => new()
{
    Id = id, DisplayName = name, Result = r, BackgroundSteps = bg, Steps = st,
    ErrorMessage = r == ExecutionResult.Failed ? "it failed" : null,
};

ScenarioStep[] Bg(string text = "a customer is signed in", ExecutionResult st = ExecutionResult.Passed) =>
    [P("Given", text, st), C("And", "the basket is full")];
ScenarioStep[] Own(string text = "the customer pays") =>
    [C("When", text), P("Then", "the receipt is sent")];

ScenarioStep Wide() { var w = P("Given", "a wide step " + new string('x', 500)); w.DocString = new string('d', 600); w.Parameters = [new StepParameter { Name = "t", Kind = StepParameterKind.Inline, InlineValue = new InlineParameterValue(new string('z', 300), null, VerificationStatus.NotApplicable) }]; return w; }
ScenarioStep[] Big() => [C("When", "the big composite", ExecutionResult.Passed, Enumerable.Range(0, 30).Select(i => i == 2 ? C("Given", "nested composite " + new string('n', 60)) : P("Given", "sub step " + i + " " + new string('s', 200))).ToArray())];

Feature[] Model() =>
[
    new()
    {
        DisplayName = "Orders",
        Scenarios =
        [
            Row("a0", "a passes", ExecutionResult.Passed, Bg(), Own()),
            Row("a1", "b fails in bg", ExecutionResult.Failed, Bg("a customer is signed in", ExecutionResult.Failed), null),
            Row("a2", "c never starts", ExecutionResult.Failed, null, null),
            Row("a3", "d zebra in bg", ExecutionResult.Passed, Bg("a zebra customer is signed in"), Own()),
            Row("a4", "e stripe in steps", ExecutionResult.Passed, Bg(), Own("the customer pays with stripe okapi")),
            Row("a5", "f passes", ExecutionResult.Passed, Bg(), Own()),
            Row("r0", "x", ExecutionResult.Passed, Bg(), Own(), outline: "Refund"),
            Row("r1", "y", ExecutionResult.Passed, Bg(), Own(), outline: "Refund"),
            Plain("p1", "Plain mango scenario", ExecutionResult.Passed, Bg(), Own("the mango customer pays")),
            Plain("p2", "Plain papaya failing", ExecutionResult.Failed, Bg("a papaya customer is signed in", ExecutionResult.Failed), null),
            Plain("p3", "Plain kiwi scenario", ExecutionResult.Passed, Bg(), [P("When", "a very long step ".PadRight(400, 'x') + " with words that keep going and going"), P("Then", "the kiwi is sent")]),
            Plain("p4", "Plain no background", ExecutionResult.Passed, null, Own("the guava okapi customer pays")),
            Plain("p5", "Plain wide background", ExecutionResult.Passed, [Wide(), C("And", "the wide composite " + new string('y', 300))], Own("the wide customer pays")),
            Row("d0", "u", ExecutionResult.Passed, null, Big(), outline: "Deferred"),
            Row("d1", "v", ExecutionResult.Passed, null, Big(), outline: "Deferred"),
            Row("d2", "w", ExecutionResult.Passed, null, Big(), outline: "Deferred"),
        ],
    },
];

void Standard(string name, Action<ReportConfigurationOptions> tune)
{
    var o = new ReportConfigurationOptions
    {
        ReportsFolderPath = Path.Combine(outRoot, name),
        GenerateSpecificationsReport = true,
        GenerateSpecificationsData = false,
    };
    tune(o);
    DefaultDiagramsFetcher.Reset();
    ReportGenerator.CreateStandardReportsWithDiagrams(Model(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, o);
    DefaultDiagramsFetcher.Reset();
}

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");
Standard("sep", o => o.SeparateBackgroundSteps = true);
Standard("combined", o => o.SeparateBackgroundSteps = false);
Standard("combined-num", o => { o.SeparateBackgroundSteps = false; o.TestRunReportShowStepNumbers = true; });
Standard("sep-closed", o => { o.SeparateBackgroundSteps = true; o.TestRunReportToggleDefaults = new ReportToggleDefaults { StepsSectionOpen = false }; });
Standard("sep-bgfalse", o => { o.SeparateBackgroundSteps = true; o.TestRunReportToggleDefaults = new ReportToggleDefaults { BackgroundStepsOpen = false }; });

// A page with diagrams, as the search facts draw one
const string Source = "@startuml\nA -> B: zebra\n@enduml";
var dir = Path.Combine(outRoot, "diagrams");
Directory.CreateDirectory(dir);
var model = Model();
var all = model.SelectMany(f => f.Scenarios).ToArray();
var diagrams = all.Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "stub.png", Source)).ToArray();
ReportGenerator.GenerateHtmlReport(diagrams, model, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(dir, "TestRunReport.html"), "D", true,
    diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Server, separateBackgroundSteps: true,
    toggleDefaults: ResolvedToggleDefaults.BuiltIn with { RulesOpen = false, StepsSectionOpen = false, DiagramsSectionOpen = false });
// BrowserJs, each row its own diagram, the Diagrams section closed
var dirJs = Path.Combine(outRoot, "browserjs");
Directory.CreateDirectory(dirJs);
var modelJs = Model();
var allJs = modelJs.SelectMany(f => f.Scenarios).ToArray();
var diagramsJs = allJs.Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", string.Join((char)10, "@startuml", "A -> B: diagram of " + s.Id, "@enduml"))).ToArray();
ReportGenerator.GenerateHtmlReport(diagramsJs, modelJs, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(dirJs, "TestRunReport.html"), "J", true,
    diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, separateBackgroundSteps: true,
    toggleDefaults: ResolvedToggleDefaults.BuiltIn with { DiagramsSectionOpen = false });
Console.WriteLine("done " + outRoot);
