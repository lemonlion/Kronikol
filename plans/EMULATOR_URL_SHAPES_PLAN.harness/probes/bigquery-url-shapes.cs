#:package Kronikol.Extensions.BigQuery@5.12.0
#:package Google.Cloud.BigQuery.V2@3.12.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// Drives the real BigQueryClient (Google.Cloud.BigQuery.V2 3.12.0) through the shipped BigQueryTrackingMessageHandler
// (Kronikol 5.12.0) into a stub server, so every URL below is the one the SDK itself sends. For each BaseUri shape it
// prints what the classifier makes of each request, what Detailed records, and whether Summarised records it at all.
// Usage: dotnet run --file bigquery-url-shapes.cs

using System.Net;
using System.Text;
using Google.Apis.Bigquery.v2.Data;
using Google.Apis.Http;
using Google.Cloud.BigQuery.V2;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

string?[] baseUris =
[
    null,                                   // the SDK default, https://bigquery.googleapis.com/bigquery/v2/
    "http://127.0.0.1:9050/",               // Testcontainers' GetEmulatorEndpoint(), the issue's case
    "http://127.0.0.1:9050",                // the same without the trailing slash
    "http://127.0.0.1:9050/bigquery/v2/",   // the issue's workaround, and BreakfastProvider's CI setting
    "http://gateway.local/bq/",             // any other base path: a proxy, a gateway, an in-house emulator
];

foreach (var baseUri in baseUris)
{
    Console.WriteLine($"=== BaseUri {baseUri ?? "(default)"}");
    var detailed = Run(baseUri, BigQueryTrackingVerbosity.Detailed);
    var summarised = Run(baseUri, BigQueryTrackingVerbosity.Summarised);
    for (var i = 0; i < detailed.Count; i++)
    {
        var d = detailed[i];
        var s = summarised.Count > i ? summarised[i] : default;
        Console.WriteLine($"  {d.Op,-22} {d.Method,-6} {Trim(d.Sent),-96} -> {d.Classified,-34} Detailed: {d.Logged,-48} Summarised: {(s.Logged == "(not recorded)" ? "NOT RECORDED" : s.Logged)}");
    }
    Console.WriteLine($"  requests sent {detailed.Count}; Summarised recorded {summarised.Count(r => r.Logged != "(not recorded)")}");
}

static string Trim(string s) => s.Length > 96 ? s[..93] + "..." : s;

static List<Row> Run(string? baseUri, BigQueryTrackingVerbosity verbosity)
{
    var testId = Guid.NewGuid().ToString();
    var stub = new StubBigQuery(() => Logs(testId).Where(l => l.Type == RequestResponseType.Request).ToList());
    var options = new BigQueryTrackingMessageHandlerOptions
    {
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("probe", testId),
    };
    var builder = new BigQueryClientBuilder
    {
        ProjectId = "shop",
        ApiKey = "any",
        HttpClientFactory = new StubbedTrackingFactory(options, stub),
    };
    if (baseUri is not null) builder.BaseUri = baseUri;
    var client = builder.Build();
    var schema = new TableSchemaBuilder { { "id", BigQueryDbType.String } }.Build();

    var ops = new (string Name, Action Call)[]
    {
        ("CreateDataset", () => client.CreateDataset("sales")),
        ("GetDataset", () => client.GetDataset("sales")),
        ("ListDatasets", () => client.ListDatasets().Take(1).ToList()),
        ("CreateTable", () => client.CreateTable("sales", "orders", schema)),
        ("GetTable", () => client.GetTable("sales", "orders")),
        ("ListTables", () => client.ListTables("sales").Take(1).ToList()),
        ("PatchTable", () => client.PatchTable("sales", "orders", new Table { Description = "d" })),
        ("InsertRow", () => client.InsertRow("sales", "orders", new BigQueryInsertRow { { "id", "1" } })),
        ("ListRows", () => client.ListRows("sales", "orders", schema).Take(1).ToList()),
        ("ExecuteQuery", () => client.ExecuteQuery("SELECT 1", parameters: null).ToList()),
        ("CreateQueryJob", () => client.CreateQueryJob("SELECT 1", parameters: null)),
        ("GetJob", () => client.GetJob("job_1")),
        ("ListJobs", () => client.ListJobs().Take(1).ToList()),
        ("CancelJob", () => client.CancelJob("job_1")),
        ("DeleteJob", () => client.DeleteJob("job_1")),
        ("UploadCsv", () => client.UploadCsv("sales", "orders", schema, new MemoryStream(Encoding.UTF8.GetBytes("1\n")))),
        ("ListProjects", () => client.ListProjects().Take(1).ToList()),
        ("ServiceAccountEmail", () => client.GetBigQueryServiceAccountEmail()),
        ("ListModels", () => client.ListModels("sales").Take(1).ToList()),
        ("ListRoutines", () => client.ListRoutines("sales").Take(1).ToList()),
        ("Tables.GetIamPolicy", () => client.Service.Tables.GetIamPolicy(new GetIamPolicyRequest(), "projects/shop/datasets/sales/tables/orders").Execute()),
        ("Datasets.Undelete", () => client.Service.Datasets.Undelete(new UndeleteDatasetRequest(), "shop", "sales").Execute()),
        ("RowAccessPolicies.List", () => client.Service.RowAccessPolicies.List("shop", "sales", "orders").Execute()),
        ("DeleteTable", () => client.DeleteTable("sales", "orders")),
        ("DeleteDataset", () => client.DeleteDataset("sales")),
    };

    var rows = new List<Row>();
    foreach (var (name, call) in ops)
    {
        var before = stub.Sent.Count;
        string? error = null;
        try { call(); } catch (Exception e) { error = e.GetType().Name; }
        var sent = stub.Sent.Skip(before).ToList();
        if (sent.Count == 0) rows.Add(new Row(name, "-", $"(nothing sent{(error is null ? "" : ", " + error)})", "-", "-"));
        for (var i = 0; i < sent.Count; i++)
        {
            var (method, uri, logged) = sent[i];
            var info = BigQueryOperationClassifier.Classify(new HttpRequestMessage(new HttpMethod(method), uri));
            var classified = $"{info.Operation} {info.ResourceType ?? "-"}/{info.ResourceName ?? "-"}";
            rows.Add(new Row(sent.Count > 1 ? $"{name}[{i + 1}]" : name, method, uri.PathAndQuery, classified, logged));
        }
    }
    return rows;
}

static List<RequestResponseLog> Logs(string testId) =>
    RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToList();

record struct Row(string Op, string Method, string Sent, string Classified, string Logged);

// The shipped TrackingBigQueryHttpClientFactory is internal and wraps a real HttpClientHandler; this one is the same
// pipeline with the stub as the inner handler.
class StubbedTrackingFactory(BigQueryTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new BigQueryTrackingMessageHandler(options, inner);
}

// The tracking handler logs a request before it calls its inner handler, so the stub knows, request by request,
// whether the handler recorded the call it is about to answer.
class StubBigQuery(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
{
    private int _seen;
    public List<(string Method, Uri Uri, string Logged)> Sent { get; } = [];

    // One body that every response type the client parses can read: unknown fields are ignored.
    private const string Body = """
        {"kind":"probe","id":"shop:sales.orders","etag":"e",
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
        var logged = logs.Count > _seen ? $"{logs[^1].Method.Value} {logs[^1].Uri.PathAndQuery}" : "(not recorded)";
        _seen = logs.Count;
        Sent.Add((request.Method.Method, uri, logged));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
            response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
        return Task.FromResult(response);
    }
}
