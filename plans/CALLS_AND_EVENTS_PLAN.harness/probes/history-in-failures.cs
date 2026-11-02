// Whether a merge's false verdict reaches Failures.md, the file a reader opens first. One run per process, published 5.27.13.
// args = <shard folder> <ledger> <run id> <pass|fail>. Every run makes the same call, and only the last fails.
#:package Kronikol@5.27.13
#:property PublishAot=false

using Kronikol;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

var (folder, ledger, runId, verdict) = (args[0], args[1], args[2], args[3]);
const string id = "f1";
const string name = "Deleting an order removes it";
var request = Guid.NewGuid();
var trace = Guid.NewGuid();
var uri = new Uri("cosmos:///orders");
var at = DateTimeOffset.UtcNow;
RequestResponseLogger.Log(new RequestResponseLog(name, id, "Delete", null, uri, [], "CosmosDB", "Orders Api",
    RequestResponseType.Request, trace, request, false, DependencyCategory: DependencyCategories.CosmosDB) { Timestamp = at });
RequestResponseLogger.Log(new RequestResponseLog(name, id, "Delete", null, uri, [], "CosmosDB", "Orders Api",
    RequestResponseType.Response, trace, request, false, DependencyCategory: DependencyCategories.CosmosDB) { Timestamp = at.AddMilliseconds(5) });
var failed = verdict == "fail";
var scenario = new Scenario
{
    Id = id, DisplayName = name, Duration = TimeSpan.FromSeconds(1),
    Result = failed ? ExecutionResult.Failed : ExecutionResult.Passed,
    ErrorMessage = failed ? "Expected the order to be gone" : null,
};
ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Probe", Scenarios = [scenario] }], DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
        GenerateComponentDiagram = false, WriteRunSummaryToConsole = false, HistoryFilePath = ledger, HistoryRunId = runId,
    });
