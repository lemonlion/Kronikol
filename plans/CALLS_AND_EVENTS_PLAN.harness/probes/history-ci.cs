// One run per process, published 5.27.13. args = <reports folder> <pass|fail> [custom run id]. No HistoryFilePath: the ledger comes from
// KRONIKOL_HISTORY when set, as the read action sets it. Every run makes the same Cosmos DB Delete, so a changed verdict is false.
#:package Kronikol@5.27.13
#:property PublishAot=false

using Kronikol;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

var folder = args[0];
var failed = args[1] == "fail";
var custom = args.Length > 2 ? args[2] : null;
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
        GenerateComponentDiagram = false, WriteRunSummaryToConsole = false, HistoryRunId = custom,
    });
