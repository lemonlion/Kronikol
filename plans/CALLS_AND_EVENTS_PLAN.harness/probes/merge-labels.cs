// What a merge does to an operation label that reads as an HTTP verb, for a record of kind Event and one of kind Default.
// The published 5.27.13 packages, so this measures what a consumer has. Writes one mergeable shard to args[0].
// #220's suggestion says a merged report reads the labels as before once the kind moves to Default: this is that claim.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Spanner@5.27.13
#:package Kronikol.Extensions.BigQuery@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618

using Kronikol;
using Kronikol.Constants;
using Kronikol.Extensions.BigQuery;
using Kronikol.Extensions.Spanner;
using Kronikol.Reports;
using Kronikol.Tracking;

var folder = args[0];
const string id = "s1";
const string name = "Labels";
(string, string) Current() => (name, id);

void Tracked(string caller, Func<(Guid Req, Guid Trace)> request, Action<Guid, Guid> response)
{
    var (req, trace) = request();
    if (req == Guid.Empty) throw new InvalidOperationException($"{caller}: not logged");
    response(req, trace);
}

// The real trackers, each at the verbosity whose label is a verb.
foreach (var verbosity in new[] { SpannerTrackingVerbosity.Raw, SpannerTrackingVerbosity.Summarised, SpannerTrackingVerbosity.Detailed })
{
    var tracker = new SpannerTracker(new SpannerTrackingOptions
    {
        ServiceName = $"Spanner tracker {verbosity}", Verbosity = verbosity, CurrentTestInfoFetcher = Current,
    });
    var op = new SpannerOperationInfo(SpannerOperation.Delete, "Orders", "db", "DELETE FROM Orders WHERE Id = 1");
    Tracked("spanner", () => tracker.LogRequest(op, "DELETE FROM Orders WHERE Id = 1"), (r, t) => tracker.LogResponse(op, r, t, "1 row"));
}

var bigQuery = new BigQueryTracker(new BigQueryTrackingMessageHandlerOptions
{
    ServiceName = "BigQuery tracker Detailed", Verbosity = BigQueryTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Current,
});
var delete = new BigQueryOperationInfo(BigQueryOperation.Delete, "p", "d", "tables", "t");
Tracked("bigquery", () => bigQuery.LogRequest(delete, "{}"), (r, t) => bigQuery.LogResponse(delete, r, t, "{}"));

void Hand(string service, string category, string label, RequestResponseMetaType kind)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri($"probe:///{service.Replace(' ', '-')}");
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, "body", uri, [], service, "Caller",
        RequestResponseType.Request, trace, request, false, MetaType: kind, DependencyCategory: category));
    RequestResponseLogger.Log(new RequestResponseLog(name, id, label, "answer", uri, [], service, "Caller",
        RequestResponseType.Response, trace, request, false, DependencyCategory: category));
}

// Fix-sim: Spanner's labels as they would be logged with the issue's line removed. Live: the labels results/verb-labels.txt
// reads from each package's own classifier, at the kind each package logs today (kinds-by-tracker has it).
Hand("Fix-sim Spanner Raw", DependencyCategories.Spanner, "Delete", RequestResponseMetaType.Default);
Hand("Fix-sim Spanner Summarised", DependencyCategories.Spanner, "DELETE", RequestResponseMetaType.Default);
Hand("Live BigQuery handler", DependencyCategories.BigQuery, "Delete", RequestResponseMetaType.Default);
Hand("Live CosmosDB", DependencyCategories.CosmosDB, "Delete", RequestResponseMetaType.Default);
Hand("Live CosmosDB", DependencyCategories.CosmosDB, "Patch", RequestResponseMetaType.Default);
Hand("Live EF Core", DependencyCategories.SQL, "Delete", RequestResponseMetaType.Default);
Hand("Live BlobStorage", DependencyCategories.BlobStorage, "Delete", RequestResponseMetaType.Default);
Hand("Live MongoDB Summarised", DependencyCategories.MongoDB, "Delete", RequestResponseMetaType.Default);
Hand("Live CloudStorage Summarised", DependencyCategories.CloudStorage, "Delete", RequestResponseMetaType.Default);
Hand("Live Elasticsearch Summarised", DependencyCategories.Elasticsearch, "Get", RequestResponseMetaType.Default);
Hand("Control SQL Summarised", DependencyCategories.SQL, "DELETE", RequestResponseMetaType.Default);
Hand("Control Redis", DependencyCategories.Redis, "Get (Hit)", RequestResponseMetaType.Default);

var features = new[]
{
    new Feature { DisplayName = "Probe", Scenarios = [new Scenario { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }] },
};
ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow, new ReportConfigurationOptions
{
    ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
    GenerateComponentDiagram = false, WriteRunSummaryToConsole = false,
});
Console.WriteLine(string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName).Where(f => f!.Contains("TestRunReport"))));
