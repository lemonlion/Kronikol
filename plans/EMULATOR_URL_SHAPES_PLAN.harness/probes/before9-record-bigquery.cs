#:package Kronikol.Extensions.BigQuery@5.27.2
#:package Google.Cloud.BigQuery.V2@3.12.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// What the last package published before #125's fix (Kronikol.Extensions.BigQuery 5.27.2) records for each request
// a BigQueryClient sends, under each base and verbosity, plus the methods it misread on Google's own endpoint. The
// output is the record the fix's frozen classifier is pinned to (tests/Kronikol.Tests.BigQuery/RecordedBefore9Record.cs):
// one line per request, "verbosity<TAB>method<TAB>url<TAB>label<TAB>uri", label and uri "-" when nothing was recorded.
// Usage: dotnet run --file before9-record-bigquery.cs > ../results/before9-record-bigquery.tsv

using System.Net;
using System.Text;
using Google.Apis.Http;
using Google.Cloud.BigQuery.V2;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

string?[] bases = [null, "http://127.0.0.1:9050/", "http://127.0.0.1:9050", "http://127.0.0.1:9050/bigquery/v2/", "http://gateway.local/bq/"];
foreach (var verbosity in new[] { BigQueryTrackingVerbosity.Detailed, BigQueryTrackingVerbosity.Summarised })
{
    foreach (var baseUri in bases)
        RunClient(baseUri, verbosity);
    RunRaw(verbosity);
}

static void RunClient(string? baseUri, BigQueryTrackingVerbosity verbosity)
{
    var (stub, options) = Make(verbosity);
    var builder = new BigQueryClientBuilder { ProjectId = "shop", ApiKey = "any", HttpClientFactory = new Factory(options, stub) };
    if (baseUri is not null) builder.BaseUri = baseUri;
    var client = builder.Build();
    var schema = new TableSchemaBuilder { { "id", BigQueryDbType.String } }.Build();
    Action[] calls =
    [
        () => client.CreateDataset("sales"),
        () => client.GetDataset("sales"),
        () => client.ListDatasets().Take(1).ToList(),
        () => client.CreateTable("sales", "orders", schema),
        () => client.GetTable("sales", "orders"),
        () => client.ListTables("sales").Take(1).ToList(),
        () => client.PatchTable("sales", "orders", new Google.Apis.Bigquery.v2.Data.Table { Description = "d" }),
        () => client.InsertRow("sales", "orders", new BigQueryInsertRow { { "id", "1" } }),
        () => client.ListRows("sales", "orders", schema).Take(1).ToList(),
        () => client.ExecuteQuery("SELECT 1", parameters: null).ToList(),
        () => client.CreateQueryJob("SELECT 1", parameters: null),
        () => client.GetJob("job_1"),
        () => client.ListJobs().Take(1).ToList(),
        () => client.CancelJob("job_1"),
        () => client.UploadCsv("sales", "orders", schema, new MemoryStream(Encoding.UTF8.GetBytes("1\n"))),
        () => client.ListProjects().Take(1).ToList(),
        () => client.GetBigQueryServiceAccountEmail(),
        () => client.ListModels("sales").Take(1).ToList(),
        () => client.ListRoutines("sales").Take(1).ToList(),
        () => client.DeleteTable("sales", "orders"),
        () => client.DeleteDataset("sales"),
    ];
    foreach (var call in calls)
    {
        try { call(); } catch { }
    }
    Print(verbosity, stub);
}

// The methods the shipped classifier missed or misread on Google's endpoint (F4), sent as the generated client writes them.
static void RunRaw(BigQueryTrackingVerbosity verbosity)
{
    var (stub, options) = Make(verbosity);
    using var invoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(options, stub));
    const string api = "https://bigquery.googleapis.com/bigquery/v2/projects/shop";
    (string Method, string Url)[] requests =
    [
        ("POST", $"{api}/datasets/sales/tables/orders:getIamPolicy"),
        ("POST", $"{api}/datasets/sales/tables/orders:setIamPolicy"),
        ("POST", $"{api}/datasets/sales/tables/orders:testIamPermissions"),
        ("POST", $"{api}/datasets/sales/routines/r1:getIamPolicy"),
        ("POST", $"{api}/datasets/sales/tables/orders/rowAccessPolicies/p1:getIamPolicy"),
        ("POST", $"{api}/datasets/sales:undelete"),
        ("GET", $"{api}/datasets/sales/tables/orders/rowAccessPolicies"),
        ("POST", $"{api}/datasets/sales/tables/orders/rowAccessPolicies"),
        ("POST", $"{api}/datasets/sales/tables/orders/rowAccessPolicies:batchDelete"),
        ("GET", $"{api}/datasets/sales/tables/orders/rowAccessPolicies/p1"),
        ("PUT", $"{api}/datasets/sales/tables/orders/rowAccessPolicies/p1"),
        ("DELETE", $"{api}/datasets/sales/tables/orders/rowAccessPolicies/p1"),
        ("PUT", "http://127.0.0.1:9050/upload/bigquery/v2/projects/shop/jobs?uploadType=resumable&upload_id=u1"),
        ("GET", "https://bigquery.googleapis.com/discovery/v1/apis?key=k"),
    ];
    foreach (var (method, url) in requests)
        invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), CancellationToken.None).GetAwaiter().GetResult();
    Print(verbosity, stub);
}

static (Stub, BigQueryTrackingMessageHandlerOptions) Make(BigQueryTrackingVerbosity verbosity)
{
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub(() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
    return (stub, new BigQueryTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("record", testId) });
}

static void Print(BigQueryTrackingVerbosity verbosity, Stub stub)
{
    foreach (var (method, url, label, uri) in stub.Sent)
        Console.WriteLine($"{verbosity}\t{method}\t{url}\t{label}\t{uri}");
}

class Factory(BigQueryTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new BigQueryTrackingMessageHandler(options, inner);
}

// The handler logs a request before calling its inner handler, so the stub knows what each request was recorded as.
class Stub(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
{
    private int _seen;
    public List<(string Method, string Url, string Label, string Uri)> Sent { get; } = [];

    private const string Body = """
        {"kind":"record","id":"shop:sales.orders","etag":"e",
         "jobReference":{"projectId":"shop","jobId":"job_1","location":"US"},"jobComplete":true,"totalRows":"0",
         "schema":{"fields":[{"name":"id","type":"STRING"}]},"rows":[],"status":{"state":"DONE"},
         "configuration":{"query":{"query":"SELECT 1"}},
         "datasetReference":{"projectId":"shop","datasetId":"sales"},
         "tableReference":{"projectId":"shop","datasetId":"sales","tableId":"orders"},
         "email":"sa@shop.iam.gserviceaccount.com"}
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var logs = requestLogs();
        var recorded = logs.Count > _seen;
        Sent.Add((request.Method.Method, uri.ToString(), recorded ? logs[^1].Method.Value?.ToString() ?? "" : "-", recorded ? logs[^1].Uri.ToString() : "-"));
        _seen = logs.Count;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
            response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
        return Task.FromResult(response);
    }
}
