// Reports for the filter search's reveal, on whichever Kronikol.dll sits beside this binary. gen <outdir>
using Kronikol;
using Kronikol.Reports;

var outDir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outDir);
Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");

static ScenarioStep Pass(string keyword, string text, params ScenarioStep[] subs) =>
    new() { Keyword = keyword, Text = text, Status = ExecutionResult.Passed, SubSteps = subs.Length > 0 ? subs : null };
static ScenarioStep Fail(string keyword, string text, params ScenarioStep[] subs) =>
    new() { Keyword = keyword, Text = text, Status = ExecutionResult.Failed, SubSteps = subs.Length > 0 ? subs : null };

static Scenario Row(string outline, string id, string row, ScenarioStep[]? background, ScenarioStep[]? steps, ExecutionResult result,
    string? block = null, int? blockIndex = null) => new()
{
    Id = id,
    DisplayName = outline,
    OutlineId = outline,
    Result = result,
    ExampleValues = new Dictionary<string, string> { ["row"] = row },
    ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
    ExamplesBlockName = block,
    ExamplesBlockIndex = blockIndex,
    BackgroundSteps = background,
    Steps = steps,
    ErrorMessage = result == ExecutionResult.Failed ? "row " + row + " failed" : null,
    ErrorStackTrace = result == ExecutionResult.Failed ? "   at Stack.Trace()" : null,
};

static ScenarioStep[] Bg(bool passes, string extra = "") => [passes ? Pass("Given", "a customer" + extra + " is signed in") : Fail("Given", "a customer" + extra + " is signed in")];
static ScenarioStep[] Own(string word = "pays") => [Pass("When", "the customer " + word), Pass("Then", "the order is confirmed")];

Feature[] Rows() =>
[
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            // Move: a word for each row, so a match moves from row to row of one scenario
            Row("Move", "m-a", "a", Bg(true), Own("firstword"), ExecutionResult.Passed),
            Row("Move", "m-b", "b", Bg(true), Own("alphaword"), ExecutionResult.Passed),
            Row("Move", "m-c", "c", Bg(true), Own("betaword"), ExecutionResult.Passed),
            Row("Move", "m-d", "d", Bg(true), Own("deltaword"), ExecutionResult.Passed),
            // Zanzibar: the word is the outline's name alone, so no row is chosen
            Row("Zanzibar", "z-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("Zanzibar", "z-b", "b", Bg(true), Own(), ExecutionResult.Passed),
            // Two scenarios share lynxword; only TwoA has the other word
            Row("TwoA", "ta-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("TwoA", "ta-b", "b", Bg(true), Own("lynxword onlytwoaword"), ExecutionResult.Passed),
            Row("TwoB", "tb-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("TwoB", "tb-b", "b", Bg(true), Own("lynxword"), ExecutionResult.Passed),
            // HoldA: the first row holds a failure in its Background, the word is in row b
            Row("HoldA", "ha-a", "a", Bg(false), null, ExecutionResult.Failed),
            Row("HoldA", "ha-b", "b", Bg(true), Own("holdword"), ExecutionResult.Passed),
            Row("HoldA", "ha-c", "c", Bg(false), null, ExecutionResult.Failed),
            // HoldB: the word is in the row whose Background holds the failure
            Row("HoldB", "hb-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("HoldB", "hb-b", "b", Bg(false, " heldword"), null, ExecutionResult.Failed),
            Row("HoldB", "hb-c", "c", Bg(true), Own(), ExecutionResult.Passed),
            // Blocks: two Examples blocks, the word in the second
            Row("Blocks", "b-a", "a", Bg(true), Own(), ExecutionResult.Passed, "first block", 0),
            Row("Blocks", "b-b", "b", Bg(true), Own(), ExecutionResult.Passed, "first block", 0),
            Row("Blocks", "b-c", "c", Bg(true), Own(), ExecutionResult.Passed, "second block", 1),
            Row("Blocks", "b-d", "d", Bg(true), Own("blockword"), ExecutionResult.Passed, "second block", 1),
            // Narrow: a word typed letter by letter moves the first row that matches
            Row("Narrow", "n-a", "a", Bg(true), Own("sku1"), ExecutionResult.Passed),
            Row("Narrow", "n-b", "b", Bg(true), Own("sku12"), ExecutionResult.Passed),
            Row("Narrow", "n-c", "c", Bg(true), Own("sku123"), ExecutionResult.Passed),
            // Plain scenarios, no rows
            new Scenario { Id = "plain-1", DisplayName = "Plain", Result = ExecutionResult.Passed, BackgroundSteps = Bg(true), Steps = Own("plainword") },
            new Scenario { Id = "plain-2", DisplayName = "Plain2", Result = ExecutionResult.Passed, BackgroundSteps = Bg(true), Steps = Own("plaintwoword") },
            new Scenario { Id = "plain-3", DisplayName = "PlainHeld", Result = ExecutionResult.Failed, ErrorMessage = "plain failed", BackgroundSteps = Bg(false, " solofailterm") },
        ],
    },
    new Feature
    {
        DisplayName = "Zebrafeature",
        Scenarios =
        [
            Row("InFeature", "if-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("InFeature", "if-b", "b", Bg(true), Own(), ExecutionResult.Passed),
            Row("InFeature", "if-c", "c", Bg(true), Own(), ExecutionResult.Passed),
        ],
    },
];

static DefaultDiagramsFetcher.DiagramAsCode[] DiagramsOf(Feature[] features) =>
    features.SelectMany(f => f.Scenarios).Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", "@startuml\nA -> B: " + s.Id + "\n@enduml")).ToArray();

void FromOptions(string name, ReportConfigurationOptions options, bool separate = true)
{
    var features = Rows();
    var folder = Path.Combine(outDir, name + ".reports");
    options.ReportsFolderPath = folder;
    options.SeparateBackgroundSteps = separate;
    options.GenerateSpecificationsReport = false;
    options.GenerateSpecificationsData = false;
    DefaultDiagramsFetcher.Reset();
    ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    File.Copy(Path.Combine(folder, "TestRunReport.html"), Path.Combine(outDir, name), true);
    Directory.Delete(folder, true);
}

FromOptions("rows.html", new ReportConfigurationOptions());
FromOptions("inline.html", new ReportConfigurationOptions(), separate: false);
FromOptions("stepsclosed.html", new ReportConfigurationOptions { TestRunReportToggleDefaults = new ReportToggleDefaults { StepsSectionOpen = false } });
FromOptions("bgopen.html", new ReportConfigurationOptions { TestRunReportToggleDefaults = new ReportToggleDefaults { BackgroundStepsOpen = true } });
FromOptions("bgfalse.html", new ReportConfigurationOptions { TestRunReportToggleDefaults = new ReportToggleDefaults { BackgroundStepsOpen = false } });
FromOptions("diagclosed.html", new ReportConfigurationOptions { TestRunReportToggleDefaults = new ReportToggleDefaults { DiagramsSectionOpen = false } });
Console.WriteLine("generated in " + outDir);
