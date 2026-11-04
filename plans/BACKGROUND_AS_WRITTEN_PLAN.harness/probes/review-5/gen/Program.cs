// Generates reports from hand-built features on whichever Kronikol.dll sits beside this binary, and prints what a
// toggle record does. gen <outdir>
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

static Scenario Row(string outline, string id, string row, ScenarioStep[]? background, ScenarioStep[]? steps, ExecutionResult result, string? rule = null,
    FileAttachment[]? attachments = null, string? description = null) => new()
{
    Id = id,
    DisplayName = outline,
    OutlineId = outline,
    Result = result,
    Rule = rule,
    Description = description,
    Attachments = attachments,
    ExampleValues = new Dictionary<string, string> { ["row"] = row },
    ExampleRawValues = new Dictionary<string, object?> { ["row"] = row },
    BackgroundSteps = background,
    Steps = steps,
    ErrorMessage = result == ExecutionResult.Failed ? "row " + row + " failed" : null,
    ErrorStackTrace = result == ExecutionResult.Failed ? "   at Stack.Trace()" : null,
};

static ScenarioStep[] Bg(bool passes) => [passes ? Pass("Given", "a customer is signed in") : Fail("Given", "a customer is signed in")];
static ScenarioStep[] Own(string row) => [Pass("When", "the customer pays"), Pass("Then", "the order is confirmed")];

Feature[] Rows() =>
[
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            // Pay: the failed Background rows are b and d
            Row("Pay", "p-a", "a", Bg(true), Own("a"), ExecutionResult.Passed),
            Row("Pay", "p-b", "b", Bg(false), null, ExecutionResult.Failed),
            Row("Pay", "p-c", "c", Bg(true), Own("c"), ExecutionResult.Passed),
            Row("Pay", "p-d", "d", Bg(false), null, ExecutionResult.Failed),
            // Mixed: rows that differ in which sections they have
            Row("Mixed", "m-a", "a both", Bg(true), Own("a"), ExecutionResult.Passed),
            Row("Mixed", "m-b", "b no background", null, Own("b"), ExecutionResult.Failed),
            Row("Mixed", "m-c", "c nothing", null, null, ExecutionResult.Failed),
            Row("Mixed", "m-d", "d both fails in steps", Bg(true), [Pass("When", "the customer pays"), Fail("Then", "the order is confirmed")], ExecutionResult.Failed),
            Row("Mixed", "m-e", "e fails in background", Bg(false), null, ExecutionResult.Failed, description: "a description"),
            Row("Mixed", "m-f", "f attachment", Bg(true), Own("f"), ExecutionResult.Passed, attachments: [new FileAttachment("log.txt", "attachments/log.txt")]),
            // Composite: a Background composite passes, fails by itself, fails in a sub-step
            Row("Composite", "c-a", "a passes", [Pass("Given", "a composite", Pass("Given", "sub one"), Pass("Given", "sub two"))], Own("a"), ExecutionResult.Passed),
            Row("Composite", "c-b", "b fails itself", [Fail("Given", "a composite", Pass("Given", "sub one"), Pass("Given", "sub two"))], null, ExecutionResult.Failed),
            Row("Composite", "c-c", "c fails in a sub-step", [Fail("Given", "a composite", Pass("Given", "sub one"), Fail("Given", "sub two"))], null, ExecutionResult.Failed),
            Row("Composite", "c-d", "d passes", [Pass("Given", "a composite", Pass("Given", "sub one"), Pass("Given", "sub two"))], Own("d"), ExecutionResult.Passed),
            // FirstFails: the first row fails in its Background, the second has a word no other row has, in its Background's sub-step
            Row("FirstFails", "f-a", "a", [Fail("Given", "a composite", Pass("Given", "sub for a"))], null, ExecutionResult.Failed),
            Row("FirstFails", "f-b", "b", [Pass("Given", "a composite", Pass("Given", "sub for zebraword"))], Own("b"), ExecutionResult.Passed),
            Row("FirstFails", "f-c", "c", [Pass("Given", "a composite", Pass("Given", "sub for c"))], Own("c"), ExecutionResult.Passed),
            // SecondFails: the first row passes, the second fails in its Background, the third has a word no other row has
            Row("SecondFails", "s-a", "a", [Pass("Given", "a composite", Pass("Given", "sub for a"))], Own("a"), ExecutionResult.Passed),
            Row("SecondFails", "s-b", "b", [Fail("Given", "a composite", Pass("Given", "sub for pumaword"))], null, ExecutionResult.Failed),
            Row("SecondFails", "s-c", "c", [Pass("Given", "a composite", Pass("Given", "sub for lynxword"))], Own("c"), ExecutionResult.Passed),
            // OwnWord: the first row fails in its Background, the second has a word no other row has in its own steps
            Row("OwnWord", "o-a", "a", Bg(false), null, ExecutionResult.Failed),
            Row("OwnWord", "o-b", "b", Bg(true), [Pass("When", "the otterword is spoken")], ExecutionResult.Passed),
            // InARule
            Row("InARule", "r-a", "a", Bg(true), Own("a"), ExecutionResult.Passed, rule: "Paying"),
            Row("InARule", "r-b", "b", Bg(false), null, ExecutionResult.Failed, rule: "Paying"),
            // Plain scenarios
            new Scenario { Id = "plain-f", DisplayName = "Plain fails", Result = ExecutionResult.Failed, ErrorMessage = "plain failed", BackgroundSteps = Bg(false) },
            new Scenario { Id = "plain-p", DisplayName = "Plain passes", Result = ExecutionResult.Passed, BackgroundSteps = Bg(true), Steps = Own("p") },
        ],
    },
];

static DefaultDiagramsFetcher.DiagramAsCode[] DiagramsOf(Feature[] features) =>
    features.SelectMany(f => f.Scenarios).Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", "@startuml\nA -> B: " + s.Id + "\n@enduml")).ToArray();

string Generate(string name, Feature[] features, bool includeRun = true, ResolvedToggleDefaults? toggles = null, bool separate = true) =>
    ReportGenerator.GenerateHtmlReport(DiagramsOf(features), features, DateTime.UtcNow, DateTime.UtcNow, null,
        Path.Combine(outDir, name), "Report", includeRun,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
        separateBackgroundSteps: separate, toggleDefaults: toggles);

// A report made from the options, the way a run makes one: only they can tell a section left unset from one set to false.
void FromOptions(string name, Feature[] features, ReportConfigurationOptions options, bool separate = true)
{
    var folder = Path.Combine(outDir, name + ".reports");
    options.ReportsFolderPath = folder;
    options.SeparateBackgroundSteps = separate;
    options.GenerateSpecificationsReport = false;
    options.GenerateSpecificationsData = false;
    ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    File.Copy(Path.Combine(folder, "TestRunReport.html"), Path.Combine(outDir, name), true);
    Directory.Delete(folder, true);
}

static string Plain(string path)
{
    var type = typeof(ReportGenerator).Assembly.GetType("Kronikol.Reports.HtmlReportFile")!;
    var m = type.GetMethod("ToPlain", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)])!;
    return (string)m.Invoke(null, [File.ReadAllText(path)])!;
}

string SectionTags(string path)
{
    var html = Plain(path);
    return string.Join(" | ", Regex.Matches(html, "<details class=\"scenario-background\"[^>]*>").Select(m => m.Value.Replace("<details class=\"scenario-background\"", "").TrimEnd('>').Trim()).GroupBy(x => x).Select(g => $"[{g.Key}] x{g.Count()}"));
}

FromOptions("rows.html", Rows(), new ReportConfigurationOptions());
FromOptions("rows-inline.html", Rows(), new ReportConfigurationOptions(), separate: false);
{
    var stepsClosed = new ReportConfigurationOptions();
    stepsClosed.TestRunReportToggleDefaults.StepsSectionOpen = false;
    FromOptions("rows-stepsclosed.html", Rows(), stepsClosed);
    var backgroundClosed = new ReportConfigurationOptions();
    backgroundClosed.TestRunReportToggleDefaults.BackgroundStepsOpen = false;
    FromOptions("rows-bgclosed.html", Rows(), backgroundClosed);
    if (Environment.GetEnvironmentVariable("GEN_ONLY_ROWS") == "1")
        return;
}

// ---- what each way of getting a record does ----
ReportConfigurationOptions Options(bool? testRun, bool? spec)
{
    var o = new ReportConfigurationOptions();
    o.TestRunReportToggleDefaults.BackgroundStepsOpen = testRun;
    o.SpecificationsToggleDefaults.BackgroundStepsOpen = spec;
    return o;
}

var hidden = typeof(ResolvedToggleDefaults).GetProperty("BackgroundStepsOpenOnFailure", BindingFlags.NonPublic | BindingFlags.Instance);
string Flag(ResolvedToggleDefaults r) => hidden is null ? "-" : hidden.GetValue(r)!.ToString()!;

var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
var builtIn = ResolvedToggleDefaults.BuiltIn;
var cases = new (string Name, ResolvedToggleDefaults Record)[]
{
    ("BuiltIn", builtIn),
    ("new()", new ResolvedToggleDefaults()),
    ("new() { HeadersShown = true }", new ResolvedToggleDefaults { HeadersShown = true }),
    ("new() { BackgroundStepsOpen = false }", new ResolvedToggleDefaults { BackgroundStepsOpen = false }),
    ("new() { BackgroundStepsOpen = true }", new ResolvedToggleDefaults { BackgroundStepsOpen = true }),
    ("BuiltIn with { HeadersShown = true }", builtIn with { HeadersShown = true }),
    ("BuiltIn with { BackgroundStepsOpen = false }", builtIn with { BackgroundStepsOpen = false }),
    ("BuiltIn with { BackgroundStepsOpen = true }", builtIn with { BackgroundStepsOpen = true }),
    ("BuiltIn with { BackgroundStepsOpen = BuiltIn.BackgroundStepsOpen }", builtIn with { BackgroundStepsOpen = builtIn.BackgroundStepsOpen }),
    ("(BuiltIn with { BackgroundStepsOpen = true }) with { BackgroundStepsOpen = false }", (builtIn with { BackgroundStepsOpen = true }) with { BackgroundStepsOpen = false }),
    ("Resolve(unset, unset) run", ReportToggleDefaultsResolver.Resolve(Options(null, null), false)),
    ("Resolve(unset, unset) spec", ReportToggleDefaultsResolver.Resolve(Options(null, null), true)),
    ("Resolve(false, unset) run", ReportToggleDefaultsResolver.Resolve(Options(false, null), false)),
    ("Resolve(false, unset) spec", ReportToggleDefaultsResolver.Resolve(Options(false, null), true)),
    ("Resolve(true, unset) run", ReportToggleDefaultsResolver.Resolve(Options(true, null), false)),
    ("Resolve(true, unset) spec", ReportToggleDefaultsResolver.Resolve(Options(true, null), true)),
    ("Resolve(unset, false) run", ReportToggleDefaultsResolver.Resolve(Options(null, false), false)),
    ("Resolve(unset, false) spec", ReportToggleDefaultsResolver.Resolve(Options(null, false), true)),
    ("Resolve(unset, true) spec", ReportToggleDefaultsResolver.Resolve(Options(null, true), true)),
    ("Resolve(true, false) spec", ReportToggleDefaultsResolver.Resolve(Options(true, false), true)),
    ("Resolve(false, true) spec", ReportToggleDefaultsResolver.Resolve(Options(false, true), true)),
    ("Resolve(unset) with { HeadersShown = true }", ReportToggleDefaultsResolver.Resolve(Options(null, null), false) with { HeadersShown = true }),
    ("JSON round trip of BuiltIn", JsonSerializer.Deserialize<ResolvedToggleDefaults>(JsonSerializer.Serialize(builtIn, json), json)!),
    ("JSON round trip of Resolve(unset)", JsonSerializer.Deserialize<ResolvedToggleDefaults>(JsonSerializer.Serialize(ReportToggleDefaultsResolver.Resolve(Options(null, null), false), json), json)!),
    ("public properties copied by reflection from BuiltIn", CopyPublic(builtIn)),
};

static ResolvedToggleDefaults CopyPublic(ResolvedToggleDefaults from)
{
    var copy = new ResolvedToggleDefaults();
    foreach (var p in typeof(ResolvedToggleDefaults).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        if (p.CanWrite && p.Name != "EqualityContract")
            p.SetValue(copy, p.GetValue(from));
    return copy;
}

var failedBackground = new Func<Feature[]>(() =>
[
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Failed, ErrorMessage = "x", BackgroundSteps = Bg(false) },
        ],
    },
]);
Console.WriteLine("record | flag | == BuiltIn | ToString == BuiltIn's | hash == | section in a failing report");
var i = 0;
foreach (var (name, record) in cases)
{
    var path = Generate($"tog-{i++}.html", failedBackground(), toggles: record);
    Console.WriteLine($"{name} | {Flag(record)} | {record == builtIn} | {record.ToString() == builtIn.ToString()} | {record.GetHashCode() == builtIn.GetHashCode()} | {SectionTags(path)}");
    File.Delete(path);
}

// no toggles at all
{
    var path = Generate("tog-null.html", failedBackground());
    Console.WriteLine($"toggleDefaults omitted | - | - | - | - | {SectionTags(path)}");
    File.Delete(path);
}
// The Specifications page, by the public method: a section with a failure, under each resolved record
foreach (var (name, options) in new[] { ("unset", Options(null, null)), ("run false", Options(false, null)), ("spec false", Options(null, false)) })
{
    var path = Generate($"spec-{name.Replace(' ', '-')}.html", failedBackground(), includeRun: false, toggles: ReportToggleDefaultsResolver.Resolve(options, true));
    Console.WriteLine($"Specifications page, {name} | {SectionTags(path)} | status icon shown: {Plain(path).Contains("step-status failed")} | failure-result: {Plain(path).Contains("class=\"failure-result\"")}");
    File.Delete(path);
}
// Specifications with generateBlankOnFailedTests as the standard reports call it
{
    var f = failedBackground();
    var path = ReportGenerator.GenerateHtmlReport(DiagramsOf(f), f, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(outDir, "spec-blank.html"), "Spec", false, generateBlankOnFailedTests: true, separateBackgroundSteps: true);
    Console.WriteLine($"Specifications page, blank on failure | length {new FileInfo(path).Length}");
    File.Delete(path);
}
// a retried scenario: passed overall, a Background step failed on an attempt the list kept
{
    var f = new[] { new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Passed, BackgroundSteps = Bg(false) }] } };
    var path = ReportGenerator.GenerateHtmlReport(DiagramsOf(f), f, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(outDir, "spec-pass.html"), "Spec", false, generateBlankOnFailedTests: true, separateBackgroundSteps: true,
        toggleDefaults: ReportToggleDefaultsResolver.Resolve(Options(null, null), true));
    Console.WriteLine($"Specifications page, scenario passed with a failed Background step | {SectionTags(path)}");
    File.Delete(path);
}

// ---- a composite that failed by itself, and how much it holds ----
foreach (var count in new[] { 3, 200, 5000 })
{
    var subs = Enumerable.Range(0, count).Select(n => Pass("And", $"a sub-step number {n} that reads like a real one with some words in it")).ToArray();
    Feature[] features(ExecutionResult status, bool failedSub) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "a", DisplayName = "a", Result = status, ErrorMessage = status == ExecutionResult.Failed ? "x" : null,
                    Steps = [new ScenarioStep { Keyword = "When", Text = "the composite", Status = status, SubSteps = subs }],
                },
            ],
        },
    ];
    var failedItself = Generate($"size-{count}-failed.html", features(ExecutionResult.Failed, false), separate: false);
    var passed = Generate($"size-{count}-passed.html", features(ExecutionResult.Passed, false), separate: false);
    var plainFailed = Plain(failedItself);
    var plainPassed = Plain(passed);
    var tag = Regex.Match(plainFailed, "<details class=\"step step-collapsible[^>]*>").Value;
    Console.WriteLine($"composite of {count}, failed by itself: file {new FileInfo(failedItself).Length} bytes, plain {plainFailed.Length}, {tag} data-steps:{plainFailed.Contains("data-steps=")} | passed: file {new FileInfo(passed).Length}, plain {plainPassed.Length}");
    if (count != 5000) { File.Delete(failedItself); File.Delete(passed); }
}

// ---- a failure storm: every scenario fails in a composite's own body, its sub-steps all passed ----
foreach (var (scenarios, subCount) in new[] { (500, 60), (2000, 20) })
{
    var feature = new Feature
    {
        DisplayName = "Storm",
        Scenarios = Enumerable.Range(0, scenarios).Select(n => new Scenario
        {
            Id = $"storm-{n}", DisplayName = $"Storm {n}", Result = ExecutionResult.Failed, ErrorMessage = "the composite threw",
            Steps = [new ScenarioStep
            {
                Keyword = "When", Text = "the composite", Status = ExecutionResult.Failed,
                SubSteps = Enumerable.Range(0, subCount).Select(i => Pass("And", $"a sub-step number {i} of scenario {n} with some more words in it")).ToArray(),
            }],
        }).ToArray(),
    };
    var storm = Generate($"storm-{scenarios}-{subCount}.html", [feature], separate: false);
    var plain = Plain(storm);
    Console.WriteLine($"storm {scenarios} x {subCount}: file {new FileInfo(storm).Length}, plain {plain.Length}, data-steps holders {Regex.Matches(plain, "data-steps=").Count}");
}
