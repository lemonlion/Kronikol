#:project ../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// S5 through the real options path (run where prototype.diff is applied; on HEAD it prints POPUP@-1): CreateStandardReportsWithDiagrams with the popup sheet, a theme
// marker and a CustomCss marker, flow tracking on and off; prints the markers' order in each file.
using Kronikol;
using Kronikol.Reports;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");

var features = new[]
{
    new Feature
    {
        DisplayName = "Orders",
        Scenarios = [new Scenario { Id = "s5-" + Guid.NewGuid().ToString("N"), DisplayName = "Create order", IsHappyPath = true, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(100),
            Steps = [new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed }] }]
    }
};

foreach (var flow in new[] { true, false })
{
    var dir = Path.Combine(Directory.GetCurrentDirectory(), "out-s5-" + (flow ? "flow" : "noflow"));
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
    var options = new ReportConfigurationOptions
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = flow,
        HtmlSpecificationsCustomStyleSheet = "/*THEME*/",
        InternalFlowPopupCustomStyleSheet = "/*POPUP*/ .iflow-toggle-active { background: rgb(1, 2, 3); }",
        CustomCss = "/*CUSTOM*/",
    };
    ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
    foreach (var name in new[] { "Specifications.html", "TestRunReport.html" })
    {
        var path = Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
        if (path is null) { Console.WriteLine($"flow={flow} {name}: not written"); continue; }
        var html = File.ReadAllText(path);
        int At(string s) => html.IndexOf(s, StringComparison.Ordinal);
        Console.WriteLine($"flow={flow} {name}: THEME@{At("/*THEME*/")} POPUP@{At("/*POPUP*/")} CUSTOM@{At("/*CUSTOM*/")} " +
            $"popup-sheet@{At(".iflow-toggle-active:hover")} notes-sheet@{At(".details-radio-btn.details-active")} length={html.Length}");
    }
}
