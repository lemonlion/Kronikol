using System.Net;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

var shape = args[0];
var dir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(dir);
var options = new ReportConfigurationOptions
{
    ReportsFolderPath = dir,
    InternalFlowTracking = false,
    GenerateComponentDiagram = false,
    WriteRunSummaryToConsole = false,
};

void Call(string testId, string path)
{
    RequestResponseLogger.LogPair("t", testId, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);
}

Feature[] features;
var start = DateTime.UtcNow;
switch (shape)
{
    case "custom-adapter":
    {
        // A consumer's own adapter: its own rows, the collector's sub-steps, as the BDDfy adapter did until now.
        const string id = "custom-1";
        StepCollector.StartStep(id, "Given", "a thing", null, null);
        Call(id, "before-sub");
        StepCollector.StartStep(id, null, "a sub step", null, null);
        Call(id, "in-sub");
        StepCollector.CompleteStep(id, true);
        Call(id, "after-sub");
        StepCollector.CompleteStep(id, true);
        StepCollector.StartStep(id, "When", "another thing", null, null);
        Call(id, "second-step");
        StepCollector.CompleteStep(id, true);
        var collected = StepCollector.GetSteps(id);
        features =
        [
            new Feature
            {
                DisplayName = "Custom",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = id,
                        DisplayName = "custom adapter",
                        Result = ExecutionResult.Passed,
                        Steps = collected.Select(c => new ScenarioStep
                        {
                            Keyword = c.Keyword,
                            Text = c.Text,
                            Status = c.Status,
                            SubSteps = c.SubSteps,
                        }).ToArray(),
                    },
                ],
            },
        ];
        break;
    }
    case "null-test-id":
    {
        const string id = "good-1";
        StepCollector.StartStep(id, "Given", "a thing", null, null);
        Call(id, "good");
        StepCollector.CompleteStep(id, true);
        RequestResponseLogger.LogPair("t", null!, HttpMethod.Post, new Uri("http://shop.test/no-id"), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);
        features =
        [
            new Feature
            {
                DisplayName = "Null id",
                Scenarios =
                [
                    new Scenario { Id = id, DisplayName = "good", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) },
                ],
            },
        ];
        break;
    }
    case "null-scenario-id":
    {
        const string id = "good-2";
        StepCollector.StartStep(id, "Given", "a thing", null, null);
        Call(id, "good");
        StepCollector.CompleteStep(id, true);
        features =
        [
            new Feature
            {
                DisplayName = "Null scenario id",
                Scenarios =
                [
                    new Scenario { Id = id, DisplayName = "good", Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(id) },
                    new Scenario { Id = null!, DisplayName = "no id", Result = ExecutionResult.Passed },
                ],
            },
        ];
        break;
    }
    case "reused-step-object":
    {
        // The public detector, with one step object listed twice in a scenario.
        var login = new ScenarioStep { Keyword = "Given", Text = "logged in", Status = ExecutionResult.Passed };
        var check = new ScenarioStep { Keyword = "Then", Text = "it works", Status = ExecutionResult.Passed };
        var other = new ScenarioStep { Keyword = "Then", Text = "it also works", Status = ExecutionResult.Passed };
        var a = new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Passed, Steps = [login, check, login, other] };
        var b = new Scenario { Id = "b", DisplayName = "b", Result = ExecutionResult.Passed, Steps = [login, other] };
        BackgroundStepsDetector.DetectAndExtract([a, b]);
        Console.WriteLine("a.Background: " + string.Join(" | ", (a.BackgroundSteps ?? []).Select(s => s.Text)));
        Console.WriteLine("a.Steps:      " + string.Join(" | ", (a.Steps ?? []).Select(s => s.Text)));
        Console.WriteLine("b.Steps:      " + string.Join(" | ", (b.Steps ?? []).Select(s => s.Text)));
        return;
    }
    default:
        throw new ArgumentException(shape);
}

try
{
    ReportGenerator.CreateStandardReportsWithDiagrams(features, start, DateTime.UtcNow, options);
    Console.WriteLine("generator returned");
}
catch (Exception ex)
{
    Console.WriteLine($"generator threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
}
Console.WriteLine("files: " + string.Join(" ", Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName) : []));
