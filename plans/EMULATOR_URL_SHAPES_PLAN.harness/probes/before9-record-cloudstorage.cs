#:package Kronikol.Extensions.CloudStorage@5.27.2
#:package Google.Cloud.Storage.V1@4.16.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// What the last package published before #125's fix (Kronikol.Extensions.CloudStorage 5.27.2) records for each request
// a StorageClient sends, under each base and verbosity, plus the methods it misread on Google's own endpoint. The output
// is the record the fix's frozen classifier is pinned to (tests/Kronikol.Tests.CloudStorage/RecordedBefore9Record.cs):
// one line per request, "verbosity<TAB>method<TAB>url<TAB>label<TAB>uri", label and uri "-" when nothing was recorded.
// Usage: dotnet run --file before9-record-cloudstorage.cs > ../results/before9-record-cloudstorage.tsv

using System.Net;
using System.Text;
using Google.Apis.Http;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

string?[] bases = [null, "http://127.0.0.1:4443/storage/v1/", "http://127.0.0.1:4443/"];
foreach (var verbosity in new[] { CloudStorageTrackingVerbosity.Detailed, CloudStorageTrackingVerbosity.Summarised })
{
    foreach (var baseUri in bases)
        RunClient(baseUri, verbosity);
    RunRaw(verbosity);
}

static void RunClient(string? baseUri, CloudStorageTrackingVerbosity verbosity)
{
    var (stub, options) = Make(verbosity);
    var builder = new StorageClientBuilder { UnauthenticatedAccess = true, HttpClientFactory = new Factory(options, stub) };
    if (baseUri is not null) builder.BaseUri = baseUri;
    var client = builder.Build();
    Action[] calls =
    [
        () => client.CreateBucket("proj", "orders"),
        () => client.GetBucket("orders"),
        () => client.UploadObject("orders", "order-1.json", "application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}"))),
        () => client.GetObject("orders", "order-1.json"),
        () => client.DownloadObject("orders", "order-1.json", new MemoryStream()),
        () => client.ListObjects("orders").Take(1).ToList(),
        () => client.CopyObject("orders", "order-1.json", "archive", "order-1.json"),
        () => client.DeleteObject("orders", "order-1.json"),
        () => client.DeleteBucket("orders"),
        () => client.GetBucket("archive"),
        () => client.DeleteBucket("archive"),
    ];
    foreach (var call in calls)
    {
        try { call(); } catch { }
    }
    Print(verbosity, stub);
}

// Requests the shipped classifier misread on Google's endpoint (F12, F13), as the generated client writes them.
static void RunRaw(CloudStorageTrackingVerbosity verbosity)
{
    var (stub, options) = Make(verbosity);
    using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(options, stub));
    const string api = "https://storage.googleapis.com/storage/v1";
    (string Method, string Url)[] requests =
    [
        ("GET", $"{api}/b/orders/acl/user-a"),
        ("DELETE", $"{api}/b/orders/acl/user-a"),
        ("GET", $"{api}/b/orders/iam"),
        ("GET", $"{api}/b/orders/operations"),
        ("GET", $"{api}/b/orders/notificationConfigs"),
        ("DELETE", $"{api}/b/orders/o/a.json/acl/user-a"),
        ("PUT", $"{api}/b/orders/o/a.json/acl/user-a"),
        ("POST", $"{api}/b/orders/o/a.json/iam"),
        ("PUT", $"{api}/b/orders/o/a.json"),
        ("POST", $"{api}/b/orders/o/a.json/rewriteTo/b/archive/o/a.json"),
        ("PUT", "http://localhost:4443/upload/storage/v1/b/orders/o?uploadType=resumable&upload_id=u1"),
        ("GET", $"{api}/projects/p/hmacKeys"),
        ("GET", "https://storage.googleapis.com/discovery/v1/apis"),
    ];
    foreach (var (method, url) in requests)
        invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), CancellationToken.None).GetAwaiter().GetResult();
    Print(verbosity, stub);
}

static (Stub, CloudStorageTrackingMessageHandlerOptions) Make(CloudStorageTrackingVerbosity verbosity)
{
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub(() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
    return (stub, new CloudStorageTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("record", testId) });
}

static void Print(CloudStorageTrackingVerbosity verbosity, Stub stub)
{
    foreach (var (method, url, label, uri) in stub.Sent)
        Console.WriteLine($"{verbosity}\t{method}\t{url}\t{label}\t{uri}");
}

class Factory(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new CloudStorageTrackingMessageHandler(options, inner);
}

// The handler logs a request before calling its inner handler, so the stub knows what each request was recorded as.
class Stub(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
{
    private int _seen;
    public List<(string Method, string Url, string Label, string Uri)> Sent { get; } = [];

    private const string Body = """
        {"kind":"storage#object","name":"order-1.json","bucket":"orders","id":"orders/order-1.json","size":"2",
         "generation":"1","contentType":"application/json","items":[],"done":true,
         "resource":{"name":"order-1.json","bucket":"archive"}}
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
