// Reports from hand-built features, the way a run makes them, on whichever Kronikol.dll sits beside this binary. gen <outdir>
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
static ScenarioStep[] Many(string word, int count, bool failLast = false) =>
    Enumerable.Range(0, count).Select(n => failLast && n == count - 1
        ? Fail("Given", $"{word} leaf {n} that reads like a real sub-step with quite a few words in it so the entry is long")
        : Pass("Given", $"{word} leaf {n} that reads like a real sub-step with quite a few words in it so the entry is long")).ToArray();

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

static ScenarioStep[] Bg(bool passes) => [passes ? Pass("Given", "a customer is signed in") : Fail("Given", "a customer is signed in")];
static ScenarioStep[] Own(string word = "pays") => [Pass("When", "the customer " + word), Pass("Then", "the order is confirmed")];

// a composite in the Background: lazily drawn sub-steps, a composite among them lazily drawn too
static ScenarioStep DeepPass(string row) => Pass("Given", "an outer composite",
    Pass("Given", "an inner composite", Many("inner " + row, 40)),
    Pass("Given", "a second inner composite", Many("second " + row, 40)));
static ScenarioStep DeepFailInner(string row) => Pass("Given", "an outer composite",
    Pass("Given", "an inner composite", Many("inner " + row, 40, failLast: true)),
    Pass("Given", "a second inner composite", Many("second " + row, 40)));
static ScenarioStep DeepFailOuter(string row) => Fail("Given", "an outer composite",
    Pass("Given", "an inner composite", Many("inner " + row, 40)),
    Pass("Given", "a second inner composite", Many("second " + row, 40)));

Feature[] Rows() =>
[
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            // Deep: a Background composite with lazily drawn composites; b fails in an inner one; d fails by itself; e has no composite
            Row("Deep", "d-a", "a", [DeepPass("a")], Own(), ExecutionResult.Passed),
            Row("Deep", "d-b", "b", [DeepFailInner("b")], null, ExecutionResult.Failed),
            Row("Deep", "d-c", "c", [DeepPass("c")], Own(), ExecutionResult.Passed),
            Row("Deep", "d-d", "d", [DeepFailOuter("d")], null, ExecutionResult.Failed),
            Row("Deep", "d-e", "e", Bg(true), Own(), ExecutionResult.Passed),
            Row("Deep", "d-f", "f", [DeepPass("f")], Own(), ExecutionResult.Passed),
            // Own: the same in the scenario's own steps (the path has no b)
            Row("Own", "o-a", "a", null, [DeepPass("a"), Pass("Then", "done")], ExecutionResult.Passed),
            Row("Own", "o-b", "b", null, [DeepFailInner("b")], ExecutionResult.Failed),
            Row("Own", "o-c", "c", null, [DeepPass("c"), Pass("Then", "done")], ExecutionResult.Passed),
            // Blocks: two Examples blocks
            Row("Blocks", "b-a", "a", Bg(true), Own(), ExecutionResult.Passed, "first block", 0),
            Row("Blocks", "b-b", "b", Bg(false), null, ExecutionResult.Failed, "first block", 0),
            Row("Blocks", "b-c", "c", Bg(true), Own(), ExecutionResult.Passed, "second block", 1),
            Row("Blocks", "b-d", "d", Bg(true), Own("blockword"), ExecutionResult.Passed, "second block", 1),
            // Search: word in a later row, first row passes
            Row("Search", "s-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("Search", "s-b", "b", Bg(true), Own("zebraword"), ExecutionResult.Passed),
            Row("Search", "s-c", "c", Bg(true), Own(), ExecutionResult.Passed),
            // Word in two scenarios
            Row("TwoA", "ta-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("TwoA", "ta-b", "b", Bg(true), Own("lynxword"), ExecutionResult.Passed),
            Row("TwoB", "tb-a", "a", Bg(true), Own(), ExecutionResult.Passed),
            Row("TwoB", "tb-b", "b", Bg(true), Own("lynxword"), ExecutionResult.Passed),
            // Hidden: the word is in a closed composite's sub-step of row b, row a fails in its Background
            Row("Hidden", "h-a", "a", Bg(false), null, ExecutionResult.Failed),
            Row("Hidden", "h-b", "b", [Pass("Given", "a composite", Pass("Given", "pumaword"))], Own(), ExecutionResult.Passed),
            Row("Hidden", "h-c", "c", Bg(true), Own(), ExecutionResult.Passed),
            // HoldThenSteps: every row has a failure in the Background, some reach steps
            Row("AllFail", "af-a", "a", Bg(false), null, ExecutionResult.Failed),
            Row("AllFail", "af-b", "b", Bg(false), null, ExecutionResult.Failed),
            // Plain
            new Scenario { Id = "plain-f", DisplayName = "Plain fails", Result = ExecutionResult.Failed, ErrorMessage = "plain failed", BackgroundSteps = Bg(false) },
        ],
    },
];

static DefaultDiagramsFetcher.DiagramAsCode[] DiagramsOf(Feature[] features) =>
    features.SelectMany(f => f.Scenarios).Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", "@startuml\nA -> B: " + s.Id + "\n@enduml")).ToArray();

void FromOptions(string name, Feature[] features, ReportConfigurationOptions options, bool separate = true)
{
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

FromOptions("rows.html", Rows(), new ReportConfigurationOptions());
FromOptions("rows-inline.html", Rows(), new ReportConfigurationOptions(), separate: false);

// a big one: 300 rows, each with a Background and own steps, for the cost of a search
Feature[] Big(int rows) =>
[
    new Feature
    {
        DisplayName = "Big",
        Scenarios = Enumerable.Range(0, rows).Select(i => Row("Bigrows", "big-" + i, i.ToString("D4"), Bg(true),
            [Pass("When", i == rows - 2 ? "the customer does the wolfword thing" : "the customer pays"), Pass("Then", "the order is confirmed")], ExecutionResult.Passed)).ToArray(),
    },
];
if (args.Length > 1 && int.TryParse(args[1], out var bigRows))
    FromOptions("big.html", Big(bigRows), new ReportConfigurationOptions());
Console.WriteLine("generated in " + outDir);
