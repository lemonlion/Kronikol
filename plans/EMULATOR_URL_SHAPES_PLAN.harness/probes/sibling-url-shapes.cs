#:package Kronikol.Extensions.BlobStorage@5.12.0
#:package Kronikol.Extensions.StorageQueues@5.12.0
#:package Kronikol.Extensions.CloudStorage@5.12.0
#:package Azure.Storage.Blobs@12.30.1
#:package Azure.Storage.Queues@12.28.1
#:package Google.Cloud.Storage.V1@4.16.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// The same question as bigquery-url-shapes.cs for the other SDK handlers whose classifier reads the URL path: does
// the URL an emulator setup makes the SDK send still classify? Real SDK clients, shipped Kronikol 5.12.0 handlers,
// a stub server. Azurite: "UseDevelopmentStorage=true" (what Testcontainers' Azurite module and the Azure docs
// give). fake-gcs-server: its documented BaseUri ".../storage/v1/" and the bare root.
// Usage: dotnet run --file sibling-url-shapes.cs

using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Google.Apis.Http;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.BlobStorage;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;

const string Production = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5a2V5;EndpointSuffix=core.windows.net";
const string Azurite = "UseDevelopmentStorage=true";

foreach (var (label, conn) in new[] { ("production account", Production), ("Azurite", Azurite) })
{
    foreach (var verbosity in new[] { BlobTrackingVerbosity.Detailed, BlobTrackingVerbosity.Summarised })
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new Stub(() => Requests(testId), AzureStatus);
        var options = new BlobClientOptions().WithTestTracking(new BlobTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("probe", testId) }, stub);
        options.Retry.MaxRetries = 0;
        var service = new BlobServiceClient(conn, options);
        var container = service.GetBlobContainerClient("orders");
        var blob = container.GetBlobClient("order-1.json");
        Print($"Blob, {label}, {verbosity}", stub, r => BlobOperationClassifier.Classify(r) is var i ? $"{i.Operation} {i.ContainerName ?? "-"}/{i.BlobName ?? "-"}" : "",
        [
            ("CreateContainer", () => container.Create()),
            ("Upload", () => blob.Upload(BinaryData.FromString("{}"))),
            ("Download", () => blob.DownloadContent()),
            ("GetProperties", () => blob.GetProperties()),
            ("ListBlobs", () => container.GetBlobs().Take(1).ToList()),
            ("DeleteBlob", () => blob.Delete()),
            ("DeleteContainer", () => container.Delete()),
        ]);
    }
}

foreach (var (label, conn) in new[] { ("production account", Production), ("Azurite", Azurite) })
{
    foreach (var verbosity in new[] { StorageQueueTrackingVerbosity.Detailed, StorageQueueTrackingVerbosity.Summarised })
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new Stub(() => Requests(testId), AzureStatus);
        var options = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("probe", testId) }, stub);
        options.Retry.MaxRetries = 0;
        var queue = new QueueClient(conn, "orders", options);
        Print($"Queue, {label}, {verbosity}", stub, r => StorageQueueOperationClassifier.Classify(r) is var i ? $"{i.Operation} {i.QueueName ?? "-"}" : "",
        [
            ("CreateQueue", () => queue.Create()),
            ("SendMessage", () => queue.SendMessage("hello")),
            ("ReceiveMessages", () => queue.ReceiveMessages()),
            ("PeekMessages", () => queue.PeekMessages()),
            ("DeleteMessage", () => queue.DeleteMessage("m1", "pop1")),
            ("ClearMessages", () => queue.ClearMessages()),
            ("DeleteQueue", () => queue.Delete()),
        ]);
    }
}

foreach (var baseUri in new string?[] { null, "http://127.0.0.1:4443/storage/v1/", "http://127.0.0.1:4443/" })
{
    foreach (var verbosity in new[] { CloudStorageTrackingVerbosity.Detailed, CloudStorageTrackingVerbosity.Summarised })
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new Stub(() => Requests(testId), _ => HttpStatusCode.OK);
        var tracking = new CloudStorageTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("probe", testId) };
        var builder = new StorageClientBuilder { UnauthenticatedAccess = true, HttpClientFactory = new StubbedGcsFactory(tracking, stub) };
        if (baseUri is not null) builder.BaseUri = baseUri;
        var client = builder.Build();
        Print($"Cloud Storage, BaseUri {baseUri ?? "(default)"}, {verbosity}", stub, r => CloudStorageOperationClassifier.Classify(r) is var i ? $"{i.Operation} {i.BucketName ?? "-"}/{i.ObjectName ?? "-"}" : "",
        [
            ("CreateBucket", () => client.CreateBucket("proj", "orders")),
            ("GetBucket", () => client.GetBucket("orders")),
            ("UploadObject", () => client.UploadObject("orders", "order-1.json", "application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}")))),
            ("GetObject", () => client.GetObject("orders", "order-1.json")),
            ("DownloadObject", () => client.DownloadObject("orders", "order-1.json", new MemoryStream())),
            ("ListObjects", () => client.ListObjects("orders").Take(1).ToList()),
            ("DeleteObject", () => client.DeleteObject("orders", "order-1.json")),
            ("DeleteBucket", () => client.DeleteBucket("orders")),
        ]);
    }
}

static HttpStatusCode AzureStatus(HttpRequestMessage r) => r.Method.Method switch
{
    "PUT" or "POST" => HttpStatusCode.Created,
    "DELETE" => r.RequestUri!.AbsolutePath.Contains("/messages") ? HttpStatusCode.NoContent : HttpStatusCode.Accepted,
    _ => HttpStatusCode.OK,
};

static List<RequestResponseLog> Requests(string testId) =>
    RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList();

static void Print(string title, Stub stub, Func<HttpRequestMessage, string> classify, (string Name, Action Call)[] ops)
{
    Console.WriteLine($"=== {title}");
    var recorded = 0;
    foreach (var (name, call) in ops)
    {
        var before = stub.Sent.Count;
        try { call(); } catch { }
        var sent = stub.Sent.Skip(before).ToList();
        for (var i = 0; i < sent.Count; i++)
        {
            var (method, uri, logged) = sent[i];
            if (logged != "(not recorded)") recorded++;
            var shown = uri.PathAndQuery.Length > 70 ? uri.PathAndQuery[..67] + "..." : uri.PathAndQuery;
            Console.WriteLine($"  {(sent.Count > 1 ? $"{name}[{i + 1}]" : name),-18} {method,-6} {shown,-70} -> {classify(new HttpRequestMessage(new HttpMethod(method), uri)),-40} logged: {logged}");
        }
    }
    Console.WriteLine($"  requests sent {stub.Sent.Count}; recorded {recorded}");
}

class StubbedGcsFactory(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new CloudStorageTrackingMessageHandler(options, inner);
}

// The tracking handlers log a request before calling their inner handler, so the stub knows, per request, whether
// the call it answers was recorded, and what as.
class Stub(Func<List<RequestResponseLog>> requestLogs, Func<HttpRequestMessage, HttpStatusCode> status) : HttpMessageHandler
{
    private int _seen;
    public List<(string Method, Uri Uri, string Logged)> Sent { get; } = [];

    private const string GcsBody = """
        {"kind":"storage#object","name":"order-1.json","bucket":"orders","id":"orders/order-1.json","size":"2",
         "generation":"1","contentType":"application/json","items":[]}
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var logs = requestLogs();
        var logged = logs.Count > _seen ? $"{logs[^1].Method.Value} {logs[^1].Uri.PathAndQuery}" : "(not recorded)";
        _seen = logs.Count;
        var uri = request.RequestUri!;
        Sent.Add((request.Method.Method, uri, logged));
        var response = new HttpResponseMessage(status(request)) { Content = new StringContent(uri.Host.Contains("core.windows") || uri.Port is 10000 or 10001 ? "" : GcsBody, Encoding.UTF8, "application/json") };
        if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
            response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
        return Task.FromResult(response);
    }
}
