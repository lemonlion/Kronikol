#:package Kronikol.Extensions.BlobStorage@5.27.8
#:package Azure.Storage.Blobs@12.30.1
#:property TargetFramework=net10.0
#:property PublishAot=false

// What the last package published before #125's R2 (Kronikol.Extensions.BlobStorage 5.27.8) records for each request a
// BlobServiceClient sends, under each connection shape and verbosity, plus the other forms the classifier reads. The
// output is the record the fix's frozen classifier is pinned to
// (tests/Kronikol.Tests.BlobStorage/RecordedBefore11Record.cs): one line per request,
// "verbosity<TAB>method<TAB>url<TAB>label<TAB>uri", label and uri "-" when nothing was recorded.
// Usage: dotnet run --file before-r2-record-blob.cs > ../results/before-r2-record-blob.tsv

using System.Net;
using Azure.Storage.Blobs;
using Kronikol.Extensions.BlobStorage;
using Kronikol.Tracking;

const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
string[] connections =
[
    "DefaultEndpointsProtocol=https;AccountName=shop;AccountKey=" + Key + ";EndpointSuffix=core.windows.net",
    "UseDevelopmentStorage=true",
    Azurite("http://127.0.0.1:54321"),
    Azurite("http://azurite:10000"),
    Azurite("http://localhost:54321"),
    Azurite("http://docker-host.internal:54321"),
];

foreach (var verbosity in new[] { BlobTrackingVerbosity.Detailed, BlobTrackingVerbosity.Summarised })
{
    foreach (var connection in connections)
        RunClient(connection, verbosity);
    RunRaw(verbosity);
}

static string Azurite(string host) => $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Key};BlobEndpoint={host}/devstoreaccount1;";

static void RunClient(string connection, BlobTrackingVerbosity verbosity)
{
    var (stub, tracking) = Make(verbosity);
    var options = new BlobClientOptions().WithTestTracking(tracking, stub);
    options.Retry.MaxRetries = 0;
    var service = new BlobServiceClient(connection, options);
    var container = service.GetBlobContainerClient("orders");
    var blob = container.GetBlobClient("order-1.json");
    Action[] calls =
    [
        () => container.Create(),
        () => blob.Upload(BinaryData.FromString("{}")),
        () => blob.DownloadContent(),
        () => blob.GetProperties(),
        () => container.GetBlobs().Take(1).ToList(),
        () => blob.Delete(),
        () => container.Delete(),
        () => service.GetBlobContainers().Take(1).ToList(),
    ];
    foreach (var call in calls)
    {
        try { call(); } catch { }
    }
    Print(verbosity, stub);
}

// The other forms the classifier reads, on Azure and on Azurite's built-in account, sent as the REST API writes them.
static void RunRaw(BlobTrackingVerbosity verbosity)
{
    var (stub, tracking) = Make(verbosity);
    using var invoker = new HttpMessageInvoker(new BlobTrackingMessageHandler(tracking, stub));
    string[] roots = ["https://shop.blob.core.windows.net", "http://127.0.0.1:10000/devstoreaccount1", "http://localhost:54321/devstoreaccount1"];
    (string Method, string Path)[] requests =
    [
        ("PUT", "/orders/order-1.json?comp=metadata"),
        ("GET", "/orders/order-1.json?comp=metadata"),
        ("PUT", "/orders/order-1.json?comp=copy"),
        ("PUT", "/orders/order-1.json?comp=block&blockid=YQ%3D%3D"),
        ("PUT", "/orders/order-1.json?comp=blocklist"),
        ("PUT", "/orders/order-1.json?comp=lease"),
        ("GET", "/orders/2026%2F10%2Forder-1.json"),
        ("GET", "/orders/a%20b.json"),
        ("GET", "/?restype=service&comp=properties"),
        ("GET", "/orders?restype=container&comp=acl"),
    ];
    foreach (var root in roots)
        foreach (var (method, path) in requests)
            invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), root + path), CancellationToken.None).GetAwaiter().GetResult();
    Print(verbosity, stub);
}

static (Stub, BlobTrackingMessageHandlerOptions) Make(BlobTrackingVerbosity verbosity)
{
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub(() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
    return (stub, new BlobTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("record", testId) });
}

static void Print(BlobTrackingVerbosity verbosity, Stub stub)
{
    foreach (var (method, url, label, uri) in stub.Sent)
        Console.WriteLine($"{verbosity}\t{method}\t{url}\t{label}\t{uri}");
}

// The handler logs a request before calling its inner handler, so the stub knows what each request was recorded as.
class Stub(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
{
    private int _seen;
    public List<(string Method, string Url, string Label, string Uri)> Sent { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var logs = requestLogs();
        var recorded = logs.Count > _seen;
        Sent.Add((request.Method.Method, request.RequestUri!.ToString(), recorded ? logs[^1].Method.Value?.ToString() ?? "" : "-", recorded ? logs[^1].Uri.ToString() : "-"));
        _seen = logs.Count;
        var status = request.Method.Method switch { "PUT" or "POST" => HttpStatusCode.Created, "DELETE" => HttpStatusCode.Accepted, _ => HttpStatusCode.OK };
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
    }
}
