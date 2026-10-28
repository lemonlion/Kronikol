#:package Kronikol@4.32.0
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

// plans/SERVICE_CARD_PLAN.md 5.3.1: two 4.x standard reports written by Kronikol 4.32.0, kept as test data with a golden
// card each. Usage: dotnet run --file gen.cs -- <out-dir> <capture.ndjson>
//
// 1. The NDJSON capture through 4.32.0's ingest with its default options, as that release's `kronikol ingest` runs it.
// 2. An in-process-shaped run: attributionSource on every call, a SQL statement and a GraphQL body, and a root background
//    block whose statement is 9,000 characters, so 4.32.0 stores it compressed there (MapBackgroundJson).
using System.Net;
using Kronikol;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

var outDir = Path.GetFullPath(args[0]);
var capture = Path.GetFullPath(args[1]);

var ingestDir = Path.Combine(outDir, "v4.32-ingest");
var options = IngestPipeline.DefaultOptions();
options.ReportsFolderPath = ingestDir;
IngestPipeline.Run(new IngestRequest { InteractionFiles = [capture], Options = options });
Console.WriteLine("ingest: " + string.Join(", ", Directory.GetFiles(ingestDir).Select(Path.GetFileName)));

var start = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
var at = new DateTimeOffset(start);
Feature[] features =
[
    new Feature
    {
        DisplayName = "Orders",
        Scenarios =
        [
            new Scenario { Id = "p-order", DisplayName = "Order pancakes", Result = ExecutionResult.Passed },
            new Scenario { Id = "p-menu", DisplayName = "Query the menu", Result = ExecutionResult.Passed }
        ]
    }
];

var logs = new List<RequestResponseLog>();
// Each half is logged when it happens, so a call the service makes inside a request sits between that request's halves.
(Guid Id, Guid Trace) Request(string testId, string testName, string method, string uri, string service, string caller, string? content,
    AttributionSource source, TestPhase phase, double atMs, string? category = null, Guid? trace = null)
{
    var id = Guid.NewGuid();
    var traceId = trace ?? Guid.NewGuid();
    logs.Add(new RequestResponseLog(testName, testId, method, content, new Uri(uri), [], service, caller, RequestResponseType.Request, traceId, id, false,
        DependencyCategory: category) { AttributionSource = source, Phase = phase, Timestamp = at.AddMilliseconds(atMs) });
    return (id, traceId);
}
void Response((Guid Id, Guid Trace) call, string testId, string testName, string method, string uri, string service, string caller, HttpStatusCode status,
    AttributionSource source, TestPhase phase, double atMs, string? category = null) =>
    logs.Add(new RequestResponseLog(testName, testId, method, null, new Uri(uri), [], service, caller, RequestResponseType.Response, call.Trace, call.Id, false, status,
        DependencyCategory: category) { AttributionSource = source, Phase = phase, Timestamp = at.AddMilliseconds(atMs) });

var order = Request("p-order", "Order pancakes", "POST", "http://localhost/orders", "Breakfast Provider", "Caller", "{\"dish\":\"pancakes\"}", AttributionSource.TestContext, TestPhase.Action, 0);
var insert = Request("p-order", "Order pancakes", "INSERT", "sql://localhost/orders", "Orders Db", "Breakfast Provider", "INSERT INTO orders (id, dish) VALUES (123, 'pancakes')",
    AttributionSource.RequestHeader, TestPhase.Action, 1, category: "SQL", trace: order.Trace);
Response(insert, "p-order", "Order pancakes", "INSERT", "sql://localhost/orders", "Orders Db", "Breakfast Provider", HttpStatusCode.OK, AttributionSource.RequestHeader, TestPhase.Action, 4, category: "SQL");
Response(order, "p-order", "Order pancakes", "POST", "http://localhost/orders", "Breakfast Provider", "Caller", HttpStatusCode.Created, AttributionSource.TestContext, TestPhase.Action, 6);
var menu = Request("p-menu", "Query the menu", "POST", "http://localhost/graphql", "Breakfast Provider", "Caller", "{\"query\":\"query Menu { menu { name } }\",\"operationName\":\"Menu\"}",
    AttributionSource.TestContext, TestPhase.Action, 1000);
Response(menu, "p-menu", "Query the menu", "POST", "http://localhost/graphql", "Breakfast Provider", "Caller", HttpStatusCode.OK, AttributionSource.TestContext, TestPhase.Action, 1005);
var expire = "UPDATE orders SET status = 'expired' WHERE id IN (" + string.Join(", ", Enumerable.Range(1, 1700)) + ")";
var expired = Request(TestIdentityScope.UnknownTestId, TestIdentityScope.UnknownTestName, "UPDATE", "sql://localhost/orders", "Orders Db", "Breakfast Provider", expire,
    AttributionSource.Expired, TestPhase.Unknown, 2000, category: "SQL");
Response(expired, TestIdentityScope.UnknownTestId, TestIdentityScope.UnknownTestName, "UPDATE", "sql://localhost/orders", "Orders Db", "Breakfast Provider", HttpStatusCode.OK,
    AttributionSource.Expired, TestPhase.Unknown, 2005, category: "SQL");
Console.WriteLine($"background statement: {expire.Length} characters");

// Through 4.32.0's run end, which stores a payload of 8,192 characters or more compressed (its public data-file call never
// does): the ingest above left its own calls in the store, so it is emptied first.
RequestResponseLogger.Clear();
foreach (var log in logs)
    RequestResponseLogger.Log(log);
var inProcessDir = Directory.CreateDirectory(Path.Combine(outDir, "v4.32-in-process")).FullName;
ReportGenerator.CreateStandardReportsWithDiagrams(features, start, start.AddSeconds(5), new ReportConfigurationOptions
{
    ReportsFolderPath = inProcessDir,
    SuiteName = "BreakfastProvider.Tests",
    CompressTestRunReport = false,
    InternalFlowTracking = false,
    GenerateComponentDiagram = false,
    GenerateSpecificationsReport = false,
    GenerateSpecificationsData = false
});
Console.WriteLine("in-process: " + string.Join(", ", Directory.GetFiles(inProcessDir).Select(Path.GetFileName)));
