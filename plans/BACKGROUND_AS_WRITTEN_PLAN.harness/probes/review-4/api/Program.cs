using Kronikol;
using Kronikol.Reports;
using Kronikol.ReqNRoll;
using Reqnroll;
using System.Text.RegularExpressions;

Console.WriteLine("Kronikol " + typeof(ReportGenerator).Assembly.GetName().Version);

static string Plain(string text) => (string)typeof(ReportGenerator).Assembly.GetType("Kronikol.Reports.HtmlReportFile")!.GetMethod("ToPlain", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { text })!;

// 1. Record equality of what the resolver gives for an option left unset and one set to its documented default.
var unset = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions(), specifications: false);
var setFalse = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = false } }, specifications: false);
Console.WriteLine($"Resolve(unset) == Resolve(BackgroundStepsOpen=false): {unset == setFalse}; BuiltIn == Resolve(unset): {ResolvedToggleDefaults.BuiltIn == unset}");
Console.WriteLine($"public BackgroundStepsOpen: unset={unset.BackgroundStepsOpen} false={setFalse.BackgroundStepsOpen}; hash equal: {unset.GetHashCode() == setFalse.GetHashCode()}");

// 2. A failed Background through the public GenerateHtmlReport, with toggles built by the caller.
var features = new[]
{
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            new Scenario
            {
                Id = "a", DisplayName = "a", Result = ExecutionResult.Failed,
                BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a customer is signed in", Status = ExecutionResult.Failed }],
            },
        ],
    },
};
string Html(ResolvedToggleDefaults? toggles)
{
    var path = ReportGenerator.GenerateHtmlReport(
        [new DefaultDiagramsFetcher.DiagramAsCode("a", "", "@startuml\n@enduml")], features,
        DateTime.UtcNow, DateTime.UtcNow,
        null, $"Api_{Guid.NewGuid():N}.html", "Test", true,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
        separateBackgroundSteps: true,
        toggleDefaults: toggles);
    var html = Plain(File.ReadAllText(path));
    File.Delete(path);
    return Regex.Match(html, "<details class=\"scenario-background\"[^>]*>").Value;
}
Console.WriteLine("no toggles:                                   " + Html(null));
Console.WriteLine("BuiltIn with { BackgroundStepsOpen = false }: " + Html(ResolvedToggleDefaults.BuiltIn with { BackgroundStepsOpen = false }));
Console.WriteLine("new() { BackgroundStepsOpen = false }:        " + Html(new ResolvedToggleDefaults { BackgroundStepsOpen = false }));
Console.WriteLine("Resolve(BackgroundStepsOpen=false):           " + Html(setFalse));
Console.WriteLine("Resolve(unset):                               " + Html(unset));

// 3. A composite step that failed while its sub-steps passed, on any lane.
var composite = new[]
{
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            new Scenario
            {
                Id = "c", DisplayName = "c", Result = ExecutionResult.Failed,
                Steps =
                [
                    new ScenarioStep
                    {
                        Keyword = "Given", Text = "a composite", Status = ExecutionResult.Failed,
                        SubSteps = [new ScenarioStep { Text = "a passing sub-step", Status = ExecutionResult.Passed }],
                    },
                ],
            },
        ],
    },
};
{
    var path = ReportGenerator.GenerateHtmlReport(
        [new DefaultDiagramsFetcher.DiagramAsCode("c", "", "@startuml\n@enduml")], composite,
        DateTime.UtcNow, DateTime.UtcNow,
        null, $"Api_{Guid.NewGuid():N}.html", "Test", true,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
    var html = Plain(File.ReadAllText(path));
    File.Delete(path);
    Console.WriteLine("failed composite, sub-steps passed: " + Regex.Match(html, "<details class=\"step step-collapsible[^>]*>").Value);
}

// 4. A hand-built ReqNRollScenarioInfo through the public report generator: scenarios that share a prefix.
ReqNRollScenarioInfo Hand(string id, string title, params ReqNRollStepInfo[] steps) => new()
{
    ScenarioId = id, ScenarioTitle = title, FeatureTitle = "Hand built", ScenarioTags = [], CombinedTags = [],
    ExecutionStatus = ScenarioExecutionStatus.OK, Steps = [.. steps],
};
var hand = new[]
{
    Hand("h1", "one", new ReqNRollStepInfo("Given", "shared", ScenarioExecutionStatus.OK), new ReqNRollStepInfo("Then", "own one", ScenarioExecutionStatus.OK)),
    Hand("h2", "two", new ReqNRollStepInfo("Given", "shared", ScenarioExecutionStatus.OK), new ReqNRollStepInfo("Then", "own two", ScenarioExecutionStatus.OK)),
};
var a = new ReqNRollStepInfo("Given", "x", ScenarioExecutionStatus.OK, TimeSpan.FromSeconds(1));
var b = new ReqNRollStepInfo("Given", "x", ScenarioExecutionStatus.OK, TimeSpan.FromSeconds(1));
Console.WriteLine($"hand-built step records equal: {a == b}, ToString: {a}");
var reportsDir = Path.Combine(Path.GetTempPath(), "krk-review-api-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(reportsDir);
var cwd = Environment.CurrentDirectory;
ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(hand, DateTime.UtcNow, DateTime.UtcNow, new ReportConfigurationOptions { SpecificationsTitle = "Hand" });
var yml = Directory.GetFiles(AppContext.BaseDirectory, "Specifications.yml", SearchOption.AllDirectories).FirstOrDefault();
if (yml is not null)
{
    Console.WriteLine(string.Join("\n", File.ReadAllLines(yml).Where(l => l.Contains("BackgroundSteps") || l.Contains("Text:") || l.Contains("Steps:")).Take(12)));
}
