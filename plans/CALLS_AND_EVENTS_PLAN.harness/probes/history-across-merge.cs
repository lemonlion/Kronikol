// Whether a merged run reads as changed behaviour against the runs its own shard recorded, for a label that reads as an
// HTTP verb. Published 5.27.13. One run per process: args = <shard folder> <ledger> <run id>. Every run makes the same
// calls, so a history that names a change here is wrong.
#:package Kronikol@5.27.13
#:property PublishAot=false

using Kronikol;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

var (folder, ledger, runId) = (args[0], args[1], args[2]);

Scenario Calls(string id, string name, string service, string category, string label, RequestResponseMetaType kind)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri($"probe:///{service.Replace(' ', '-')}");
    var at = DateTimeOffset.UtcNow;
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, null, uri, [], service, "Caller",
        RequestResponseType.Request, trace, request, false, MetaType: kind, DependencyCategory: category) { Timestamp = at });
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, null, uri, [], service, "Caller",
        RequestResponseType.Response, trace, request, false, DependencyCategory: category) { Timestamp = at.AddMilliseconds(5) });
    return new Scenario { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) };
}

// The labels are the ones results/verb-labels.txt reads from each package's own classifier.
var scenarios = new[]
{
    Calls("c1", "Control, an HTTP GET", "Http Api", null!, "GET", RequestResponseMetaType.Default),
    Calls("c2", "Control, Redis Get (Hit)", "Redis", DependencyCategories.Redis, "Get (Hit)", RequestResponseMetaType.Default),
    Calls("c3", "Control, SQL DELETE in capitals", "Sql", DependencyCategories.SQL, "DELETE", RequestResponseMetaType.Default),
    Calls("e1", "Spanner Delete as an Event, as 5.27.13 logs it", "Spanner", DependencyCategories.Spanner, "Delete", RequestResponseMetaType.Event),
    Calls("d1", "Spanner Delete as Default, the issue's change", "Spanner", DependencyCategories.Spanner, "Delete", RequestResponseMetaType.Default),
    Calls("l1", "BigQuery Delete, the handler's today", "BigQuery", DependencyCategories.BigQuery, "Delete", RequestResponseMetaType.Default),
    Calls("l2", "CosmosDB Delete, today", "CosmosDB", DependencyCategories.CosmosDB, "Delete", RequestResponseMetaType.Default),
    Calls("l3", "CosmosDB Patch, today", "CosmosDB", DependencyCategories.CosmosDB, "Patch", RequestResponseMetaType.Default),
    Calls("l4", "EF Core Delete, today", "Sql", DependencyCategories.SQL, "Delete", RequestResponseMetaType.Default),
    Calls("l5", "Blob Storage Delete, today", "Blob", DependencyCategories.BlobStorage, "Delete", RequestResponseMetaType.Default),
};
ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Probe", Scenarios = scenarios }], DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
        GenerateComponentDiagram = false, WriteRunSummaryToConsole = false, HistoryFilePath = ledger, HistoryRunId = runId,
    });
