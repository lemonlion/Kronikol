// Three scenarios sharing an opening row whose doc string, comment and outcome differ per scenario: every data format, in its own folder.
using System.Net;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

var format = args[0];
var separate = args.Length > 2 && args[2] == "separate";
var fails = !(args.Length > 3 && args[3] == "pass");
var dir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(dir);
var options = new ReportConfigurationOptions
{
    ReportsFolderPath = dir,
    InternalFlowTracking = false,
    GenerateComponentDiagram = false,
    WriteRunSummaryToConsole = false,
    TestRunReportDataFormat = Enum.Parse<DataFormat>(format),
    SpecificationsDataFormat = Enum.Parse<DataFormat>(format),
    GenerateSpecificationsMarkdown = true,
    GenerateMergeableData = format == "Json",
    SeparateBackgroundSteps = separate,
};

void Call(string testId, string path) =>
    RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);

var start = DateTime.UtcNow;
var scenarios = new List<Scenario>();
for (var n = 0; n < 3; n++)
{
    var id = "f" + n;
    Call(id, "open" + n);
    Call(id, "own" + n);
    var opening = new ScenarioStep
    {
        Keyword = "Given",
        Text = "a shop",
        Status = fails && n == 1 ? ExecutionResult.Failed : ExecutionResult.Passed,
        Duration = TimeSpan.FromMilliseconds(100 * (n + 1)),
        DocString = "opening of scenario " + n,
        Comments = ["comment of scenario " + n],
    };
    scenarios.Add(new Scenario
    {
        Id = id,
        DisplayName = "scenario " + n,
        IsHappyPath = true,
        Result = fails && n == 1 ? ExecutionResult.Failed : ExecutionResult.Passed,
        ErrorMessage = fails && n == 1 ? "no shop" : null,
        Steps =
        [
            opening,
            new ScenarioStep { Keyword = "Then", Text = "it works " + n, Status = fails && n == 1 ? ExecutionResult.Skipped : ExecutionResult.Passed },
        ],
    });
}

var all = scenarios.ToArray();
BackgroundStepsDetector.DetectAndExtract(all);
Console.WriteLine("background rows: " + string.Join(",", all.Select(s => s.BackgroundSteps?.Length ?? 0)) + " own objects: " + (all[0].BackgroundSteps is { Length: > 0 } && !ReferenceEquals(all[0].BackgroundSteps![0], all[1].BackgroundSteps![0])));
try
{
    ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Formats", Scenarios = all }], start, DateTime.UtcNow, options);
    Console.WriteLine("generator returned");
}
catch (Exception ex)
{
    Console.WriteLine($"generator threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
}
Console.WriteLine("files: " + string.Join(" ", Directory.GetFiles(dir).Select(Path.GetFileName)));
