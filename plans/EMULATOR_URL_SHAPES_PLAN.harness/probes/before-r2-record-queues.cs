#:package Kronikol.Extensions.StorageQueues@5.27.8
#:package Azure.Storage.Queues@12.28.1
#:property TargetFramework=net10.0
#:property PublishAot=false

// What the last package published before #125's R2 (Kronikol.Extensions.StorageQueues 5.27.8) records for each request
// a QueueServiceClient sends, under each connection shape and verbosity, plus the other forms the classifier reads. The
// output is the record the fix's frozen classifier is pinned to
// (tests/Kronikol.Tests.StorageQueues/RecordedBefore11Record.cs): one line per request,
// "verbosity<TAB>method<TAB>url<TAB>label<TAB>uri", label and uri "-" when nothing was recorded.
// Usage: dotnet run --file before-r2-record-queues.cs > ../results/before-r2-record-queues.tsv

using System.Net;
using Azure.Storage.Queues;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;

const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
string[] connections =
[
    "DefaultEndpointsProtocol=https;AccountName=shop;AccountKey=" + Key + ";EndpointSuffix=core.windows.net",
    "UseDevelopmentStorage=true",
    Azurite("http://127.0.0.1:54322"),
    Azurite("http://azurite:10001"),
    Azurite("http://localhost:54322"),
    Azurite("http://docker-host.internal:54322"),
];

foreach (var verbosity in new[] { StorageQueueTrackingVerbosity.Detailed, StorageQueueTrackingVerbosity.Summarised })
{
    foreach (var connection in connections)
        RunClient(connection, verbosity);
    RunRaw(verbosity);
}

static string Azurite(string host) => $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Key};QueueEndpoint={host}/devstoreaccount1;";

static void RunClient(string connection, StorageQueueTrackingVerbosity verbosity)
{
    var (stub, tracking) = Make(verbosity);
    var options = new QueueClientOptions().WithTestTracking(tracking, stub);
    options.Retry.MaxRetries = 0;
    var service = new QueueServiceClient(connection, options);
    var queue = service.GetQueueClient("orders");
    Action[] calls =
    [
        () => queue.Create(),
        () => queue.SendMessage("hello"),
        () => queue.ReceiveMessages(),
        () => queue.PeekMessages(),
        () => queue.DeleteMessage("m1", "pop1"),
        () => queue.ClearMessages(),
        () => queue.Delete(),
        () => service.GetQueues().Take(1).ToList(),
    ];
    foreach (var call in calls)
    {
        try { call(); } catch { }
    }
    Print(verbosity, stub);
}

// The other forms the classifier reads, on Azure and on Azurite's built-in account, sent as the REST API writes them.
static void RunRaw(StorageQueueTrackingVerbosity verbosity)
{
    var (stub, tracking) = Make(verbosity);
    using var invoker = new HttpMessageInvoker(new StorageQueueTrackingMessageHandler(tracking, stub));
    string[] roots = ["https://shop.queue.core.windows.net", "http://127.0.0.1:10001/devstoreaccount1", "http://localhost:54322/devstoreaccount1"];
    (string Method, string Path)[] requests =
    [
        ("GET", "/orders?comp=metadata"),
        ("PUT", "/orders?comp=metadata"),
        ("PUT", "/orders/messages/m1?popreceipt=p1&visibilitytimeout=0"),
        ("GET", "/orders/messages?numofmessages=5"),
        ("GET", "/?comp=list"),
        ("GET", "/?restype=service&comp=properties"),
        ("GET", "/orders?comp=acl"),
    ];
    foreach (var root in roots)
        foreach (var (method, path) in requests)
            invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), root + path), CancellationToken.None).GetAwaiter().GetResult();
    Print(verbosity, stub);
}

static (Stub, StorageQueueTrackingMessageHandlerOptions) Make(StorageQueueTrackingVerbosity verbosity)
{
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub(() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
    return (stub, new StorageQueueTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("record", testId) });
}

static void Print(StorageQueueTrackingVerbosity verbosity, Stub stub)
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
        var status = request.Method.Method switch
        {
            "PUT" or "POST" => HttpStatusCode.Created,
            "DELETE" => request.RequestUri!.AbsolutePath.Contains("/messages") ? HttpStatusCode.NoContent : HttpStatusCode.Accepted,
            _ => HttpStatusCode.OK,
        };
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
    }
}
