using System.Net;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;

// §5.2: "a consumer who drives CreateStandardReportsWithDiagrams(IEnumerable<ReqNRollScenarioInfo>, ...) sees no change".
var dir = Path.GetFullPath(args[0]);
var options = new ReportConfigurationOptions
{
    ReportsFolderPath = dir,
    InternalFlowTracking = false,
    GenerateComponentDiagram = false,
    WriteRunSummaryToConsole = false,
};

void Call(string id, string path) =>
    RequestResponseLogger.LogPair("t", id, HttpMethod.Post, new Uri("http://shop.test/" + path), "Shop", "Tests", "{}", "{}", HttpStatusCode.OK);

ReqNRollScenarioInfo Info(string id, string title, List<ReqNRollStepInfo> steps) => new()
{
    ScenarioId = id,
    ScenarioTitle = title,
    FeatureTitle = "Hand built",
    ScenarioTags = [],
    CombinedTags = [],
    ExecutionStatus = ScenarioExecutionStatus.OK,
    Steps = steps,
};

// H1: natives pair by position, a step ran ahead of them that the consumer's list does not name
StepCollector.StartStep("h1", null, "Hook seeds", null, null);
Call("h1", "seed");
StepCollector.CompleteStep("h1", true);
StepCollector.StartStep("h1", "Given", "a customer", null, null);
Call("h1", "customer");
StepCollector.CompleteStep("h1", true);
StepCollector.StartStep("h1", "When", "an order", null, null);
Call("h1", "order");
StepCollector.CompleteStep("h1", true);

// H2: no natives at all, a collected step
StepCollector.StartStep("h2", null, "Only a collected step", null, null);
Call("h2", "only");
StepCollector.CompleteStep("h2", true);

ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(
[
    Info("h1", "H1 natives by position with an extra collected step",
    [
        new("Given", "a customer"),
        new("When", "an order"),
    ]),
    Info("h2", "H2 no natives", []),
], DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow, options);
Console.WriteLine("written");
