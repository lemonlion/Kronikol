// F1 on an old release. One run per process. args = <reports folder> <ledger> <run id>. Every run makes the same calls.
#:package Kronikol@3.11.0
#:property PublishAot=false
#:property NoWarn=CS0618

using Kronikol;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

var (folder, ledger, runId) = (args[0], args[1], args[2]);

Scenario Calls(string id, string name, string service, string? category, OneOf<HttpMethod, string> label)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri($"probe:///{service.Replace(' ', '-')}");
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, null, uri, [], service, "Caller",
        RequestResponseType.Request, trace, request, false, DependencyCategory: category));
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, null, uri, [], service, "Caller",
        RequestResponseType.Response, trace, request, false, DependencyCategory: category));
    return new Scenario { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) };
}

var scenarios = new[]
{
    Calls("c1", "Control, an HTTP GET", "Http Api", null, HttpMethod.Get),
    Calls("c2", "Control, SQL DELETE in capitals", "Sql", DependencyCategories.SQL, "DELETE"),
    Calls("l2", "CosmosDB Delete", "CosmosDB", DependencyCategories.CosmosDB, "Delete"),
    Calls("l5", "Blob Storage Delete", "Blob", DependencyCategories.BlobStorage, "Delete"),
};
ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Probe", Scenarios = scenarios }], DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
        GenerateComponentDiagram = false, WriteRunSummaryToConsole = false, HistoryFilePath = ledger, HistoryRunId = runId,
    });
