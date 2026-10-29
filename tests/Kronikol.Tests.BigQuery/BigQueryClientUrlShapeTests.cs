using System.Net;
using System.Reflection;
using System.Text;
using Google.Apis.Bigquery.v2;
using Google.Apis.Bigquery.v2.Data;
using Google.Apis.Http;
using Google.Cloud.BigQuery.V2;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// The real <see cref="BigQueryClient"/> writes every URL here, so these facts read what the SDK sends rather than what
/// a test's author believed it sends (#125). The client goes through the shipped handler to a stub server, under each
/// base URI a suite points it at: Google's, an emulator's root with and without the slash, the emulator's
/// <c>/bigquery/v2/</c> path, and a gateway's prefix. They use only client methods the package's floor (3.0.0) has, and
/// assert how each request was named, never which requests a client version sends.
/// </summary>
public class BigQueryClientUrlShapeTests
{
    public static TheoryData<string?> Bases => new()
    {
        null,
        "http://127.0.0.1:9050/",
        "http://127.0.0.1:9050",
        "http://127.0.0.1:9050/bigquery/v2/",
        "http://gateway.local/bq/",
    };

    [Fact]
    public void Every_operation_reaches_the_server()
    {
        var run = Run(null, BigQueryTrackingVerbosity.Detailed);

        // Only a method the client lacks sends nothing: datasets.undelete on the floor's client.
        string[] unsent = UndeleteMethod is null ? ["Datasets.Undelete"] : [];
        Assert.Equal(unsent, run.Operations.Except(run.Sent.Select(s => s.Operation)));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Every_call_the_client_sends_is_named_at_Detailed(string? baseUri)
    {
        var run = Run(baseUri, BigQueryTrackingVerbosity.Detailed);

        Assert.NotEmpty(run.Sent);
        Assert.All(run.Sent, sent => Assert.NotNull(sent.Recorded));
        Assert.DoesNotContain(run.Sent, sent => sent.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Every_call_the_client_sends_is_recorded_at_Summarised(string? baseUri)
    {
        var run = Run(baseUri, BigQueryTrackingVerbosity.Summarised);

        Assert.All(run.Sent, sent => Assert.NotNull(sent.Recorded));
        Assert.DoesNotContain(run.Sent, sent => sent.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void A_call_reads_the_same_under_any_base_as_under_Googles(string? baseUri)
    {
        var google = Run(null, BigQueryTrackingVerbosity.Detailed);
        var other = Run(baseUri, BigQueryTrackingVerbosity.Detailed);

        Assert.Equal(google.Sent.Select(s => (s.Operation, s.Recorded)), other.Sent.Select(s => (s.Operation, s.Recorded)));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void No_recorded_Detailed_URL_keeps_the_clients_query_string(string? baseUri)
    {
        var run = Run(baseUri, BigQueryTrackingVerbosity.Detailed);

        Assert.All(run.Sent, sent => Assert.DoesNotContain("key=", sent.Recorded!.Value.Path));
        Assert.All(run.Sent, sent => Assert.DoesNotContain("prettyPrint", sent.Recorded!.Value.Path));
    }

    [Fact]
    public void A_dataset_is_named_once_in_its_Detailed_path()
    {
        var run = Run("http://127.0.0.1:9050/", BigQueryTrackingVerbosity.Detailed);

        var get = Assert.Single(run.Sent, s => s.Operation == "GetDataset");
        Assert.Equal(("Read", "/sales"), get.Recorded);
        var delete = Assert.Single(run.Sent, s => s.Operation == "DeleteDataset");
        Assert.Equal(("Delete", "/sales"), delete.Recorded);
    }

    [Fact]
    public void The_project_list_and_the_service_account_are_named()
    {
        var run = Run(null, BigQueryTrackingVerbosity.Detailed);

        Assert.Equal(("List", "/project"), Assert.Single(run.Sent, s => s.Operation == "ListProjects").Recorded);
        Assert.Equal(("Read", "/serviceAccount"), Assert.Single(run.Sent, s => s.Operation == "ServiceAccountEmail").Recorded);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void A_load_jobs_data_is_an_insert_into_the_job_not_a_list_of_jobs(string? baseUri)
    {
        var run = Run(baseUri, BigQueryTrackingVerbosity.Detailed);

        var upload = run.Sent.Where(s => s.Operation == "UploadCsv").ToArray();
        Assert.Equal(("Create", "/job"), upload[0].Recorded);
        Assert.Contains(upload.Skip(1), s => s.Method == "PUT" && s.Recorded == ("Insert", "/job"));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void A_query_run_as_a_job_is_the_job_and_then_its_results(string? baseUri)
    {
        var run = Run(baseUri, BigQueryTrackingVerbosity.Detailed);

        var query = run.Sent.Where(s => s.Operation == "ExecuteQueryAsJob").ToArray();
        Assert.Equal(("Create", "/job"), query[0].Recorded);
        Assert.Contains(query.Skip(1), s => s.Method == "GET" && s.Recorded == ("Query", "/query/job_1"));
    }

    // One run of the client's operations under a base and a verbosity, each request paired with what the handler
    // recorded for it.
    private static Recording Run(string? baseUri, BigQueryTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new StubBigQueryServer(() => RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("BigQuery URL shapes", testId),
        };
        var builder = new BigQueryClientBuilder
        {
            ProjectId = "shop",
            ApiKey = "any",
            HttpClientFactory = new TrackingFactoryOverStub(options, stub),
        };
        if (baseUri is not null)
            builder.BaseUri = baseUri;
        var client = builder.Build();
        var schema = new TableSchemaBuilder { { "id", BigQueryDbType.String } }.Build();

        var operations = new (string Name, Action Call)[]
        {
            ("CreateDataset", () => client.CreateDataset("sales")),
            ("GetDataset", () => client.GetDataset("sales")),
            ("ListDatasets", () => client.ListDatasets().Take(1).ToList()),
            ("CreateTable", () => client.CreateTable("sales", "orders", schema)),
            ("GetTable", () => client.GetTable("sales", "orders")),
            ("ListTables", () => client.ListTables("sales").Take(1).ToList()),
            ("PatchTable", () => client.PatchTable("sales", "orders", new Google.Apis.Bigquery.v2.Data.Table { Description = "d" })),
            ("InsertRow", () => client.InsertRow("sales", "orders", new BigQueryInsertRow { { "id", "1" } })),
            ("ListRows", () => client.ListRows("sales", "orders", schema).Take(1).ToList()),
            ("ExecuteQuery", () => client.ExecuteQuery("SELECT 1", parameters: null).ToList()),
            // A priority rules out jobs.query, so the client runs the query as a job on every version.
            ("ExecuteQueryAsJob", () => client.ExecuteQuery("SELECT 1", parameters: null, new QueryOptions { Priority = QueryPriority.Batch }).ToList()),
            ("CreateQueryJob", () => client.CreateQueryJob("SELECT 1", parameters: null)),
            ("GetJob", () => client.GetJob("job_1")),
            ("ListJobs", () => client.ListJobs().Take(1).ToList()),
            ("CancelJob", () => client.CancelJob("job_1")),
            // BigQueryClient.DeleteJob is newer than the floor; the generated method it calls is not.
            ("DeleteJob", () => client.Service.Jobs.Delete("shop", "job_1").Execute()),
            ("UploadCsv", () => client.UploadCsv("sales", "orders", schema, new MemoryStream(Encoding.UTF8.GetBytes("1\n")))),
            ("ListProjects", () => client.ListProjects().Take(1).ToList()),
            ("ServiceAccountEmail", () => client.GetBigQueryServiceAccountEmail()),
            ("ListModels", () => client.ListModels("sales").Take(1).ToList()),
            ("ListRoutines", () => client.ListRoutines("sales").Take(1).ToList()),
            ("Tables.GetIamPolicy", () => client.Service.Tables.GetIamPolicy(new GetIamPolicyRequest(), "projects/shop/datasets/sales/tables/orders").Execute()),
            ("Datasets.Undelete", () => Undelete(client)),
            ("RowAccessPolicies.List", () => client.Service.RowAccessPolicies.List("shop", "sales", "orders").Execute()),
            ("DeleteTable", () => client.DeleteTable("sales", "orders")),
            ("DeleteDataset", () => client.DeleteDataset("sales")),
        };

        var sent = new List<SentRequest>();
        foreach (var (name, call) in operations)
        {
            var before = stub.Sent.Count;
            // The stub's one body cannot satisfy every response type, and what a fact reads is what was sent and
            // recorded, which happens before the client parses the answer.
            try { call(); }
            catch (Exception) { }
            sent.AddRange(stub.Sent.Skip(before).Select(s => s with { Operation = name }));
        }
        return new Recording(sent, operations.Select(o => o.Name).ToArray());
    }

    // datasets.undelete is newer than the floor's generated client (Google.Apis.Bigquery.v2 1.57), so it is called
    // where the client declares it.
    private static readonly MethodInfo? UndeleteMethod = typeof(DatasetsResource).GetMethod("Undelete");

    private static void Undelete(BigQueryClient client)
    {
        if (UndeleteMethod is null)
            return;
        var body = Activator.CreateInstance(UndeleteMethod.GetParameters()[0].ParameterType);
        var request = UndeleteMethod.Invoke(client.Service.Datasets, [body, "shop", "sales"])!;
        request.GetType().GetMethod("Execute", Type.EmptyTypes)!.Invoke(request, null);
    }

    private sealed record Recording(IReadOnlyList<SentRequest> Sent, IReadOnlyList<string> Operations);

    internal sealed record SentRequest(string Operation, string Method, Uri Uri, (string Label, string Path)? Recorded);

    private sealed class TrackingFactoryOverStub(BigQueryTrackingMessageHandlerOptions options, HttpMessageHandler stub) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new BigQueryTrackingMessageHandler(options, stub);
    }

    // The handler logs a request before it calls its inner handler, so the stub knows, request by request, whether the
    // handler recorded the call it is about to answer, and as what.
    private sealed class StubBigQueryServer(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
    {
        private int _seen;
        public List<SentRequest> Sent { get; } = [];

        // One body every response type the client parses can read: unknown fields are ignored.
        private const string Body = """
            {"kind":"stub","id":"shop:sales.orders","etag":"e",
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
            (string, string)? recorded = logs.Count > _seen
                ? (logs[^1].Method.Value?.ToString() ?? "", logs[^1].Uri.PathAndQuery)
                : null;
            _seen = logs.Count;
            Sent.Add(new SentRequest("", request.Method.Method, uri, recorded));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
            if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
                response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
            return Task.FromResult(response);
        }
    }
}
