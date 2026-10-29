#:package Kronikol.Extensions.BigQuery@5.12.0
#:package Google.Cloud.BigQuery.V2@3.12.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// The issue's repro, re-run: the real BigQueryClient (3.12.0) against the real emulator (ghcr.io/goccy/
// bigquery-emulator:0.8.1, project "probe") through the shipped BigQueryTrackingMessageHandler (Kronikol 5.12.0).
// A recorder under the tracking handler notes each request the SDK sent, the emulator's status, and whether
// Kronikol recorded the call (it logs a request before calling its inner handler).
// Usage: dotnet run --file bigquery-emulator-live.cs -- http://127.0.0.1:19070

using System.Text;
using Google.Apis.Http;
using Google.Cloud.BigQuery.V2;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

var emulator = (args.Length > 0 ? args[0] : "http://127.0.0.1:19070").TrimEnd('/');
var run = 0;
foreach (var baseUri in new[] { emulator + "/", emulator + "/bigquery/v2/" })
{
    foreach (var verbosity in new[] { BigQueryTrackingVerbosity.Detailed, BigQueryTrackingVerbosity.Summarised })
    {
        var testId = Guid.NewGuid().ToString();
        var sent = new List<string>();
        var options = new BigQueryTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("probe", testId) };
        var client = new BigQueryClientBuilder
        {
            ProjectId = "probe",
            BaseUri = baseUri,
            ApiKey = "any",
            HttpClientFactory = new RecordingTrackingFactory(options, sent,
                () => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList()),
        }.Build();
        var dataset = $"sales_{++run}";
        var schema = new TableSchemaBuilder { { "id", BigQueryDbType.String } }.Build();
        Console.WriteLine($"=== BaseUri {baseUri}, {verbosity}");
        Step("CreateDataset", () => client.CreateDataset(dataset));
        Step("CreateTable", () => client.CreateTable(dataset, "orders", schema));
        Step("InsertRow", () => client.InsertRow(dataset, "orders", new BigQueryInsertRow { { "id", "1" } }));
        Step("ExecuteQuery", () => Console.Write($"[{client.ExecuteQuery($"SELECT id FROM `probe.{dataset}.orders`", parameters: null).Count()} row] "));
        Step("ListRows", () => client.ListRows(dataset, "orders").ToList());
        Step("UploadCsv", () => client.UploadCsv(dataset, "orders", schema, new MemoryStream(Encoding.UTF8.GetBytes("2\n"))).PollUntilCompleted().ThrowOnAnyError());
        Step("ListProjects", () => client.ListProjects().Take(1).ToList());
        Step("DeleteTable", () => client.DeleteTable(dataset, "orders"));
        Step("DeleteDataset", () => client.DeleteDataset(dataset));
        var recorded = RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Console.WriteLine($"  requests sent {sent.Count}; recorded {recorded}");

        void Step(string name, Action call)
        {
            var before = sent.Count;
            string outcome;
            try { call(); outcome = "ok"; } catch (Exception e) { outcome = $"threw {e.GetType().Name}: {e.Message.Split('\n')[0]}"; }
            Console.WriteLine($"  {name,-14} {outcome}");
            foreach (var line in sent.Skip(before)) Console.WriteLine($"      {line}");
        }
    }
}

class RecordingTrackingFactory(BigQueryTrackingMessageHandlerOptions options, List<string> sent, Func<List<RequestResponseLog>> requestLogs) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
        new BigQueryTrackingMessageHandler(options, new Recorder(base.CreateHandler(args), sent, requestLogs));
}

class Recorder(HttpMessageHandler inner, List<string> sent, Func<List<RequestResponseLog>> requestLogs) : DelegatingHandler(inner)
{
    private int _seen;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var logs = requestLogs();
        var logged = logs.Count > _seen ? $"{logs[^1].Method.Value} {logs[^1].Uri.PathAndQuery}" : "NOT RECORDED";
        _seen = logs.Count;
        var response = await base.SendAsync(request, cancellationToken);
        var path = request.RequestUri!.PathAndQuery;
        sent.Add($"{request.Method.Method,-6} {(path.Length > 80 ? path[..77] + "..." : path),-80} {(int)response.StatusCode} -> {logged}");
        return response;
    }
}
