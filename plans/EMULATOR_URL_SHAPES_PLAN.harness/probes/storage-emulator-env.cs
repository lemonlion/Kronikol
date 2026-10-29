#:package Kronikol.Extensions.CloudStorage@5.12.0
#:package Google.Cloud.Storage.V1@4.16.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// The third way a Cloud Storage client meets an emulator: not a BaseUri the test sets, but STORAGE_EMULATOR_HOST,
// which StorageClientBuilder reads when EmulatorDetection asks it to (fake-gcs-server's and the Firebase emulator's
// documented setup). What base does the client end up with, and what URLs does it send? Real client, the shipped
// 5.12.0 handler (its source is unchanged through 5.14.0), a stub server.
// Usage: dotnet run --file storage-emulator-env.cs

using System.Net;
using System.Text;
using Google.Api.Gax;
using Google.Apis.Http;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

foreach (var host in new[] { "localhost:4443", "http://localhost:4443", "http://localhost:4443/", "127.0.0.1:4443" })
{
    Environment.SetEnvironmentVariable("STORAGE_EMULATOR_HOST", host);
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub(() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
    var tracking = new CloudStorageTrackingMessageHandlerOptions { Verbosity = CloudStorageTrackingVerbosity.Detailed, CurrentTestInfoFetcher = () => ("probe", testId) };
    Console.WriteLine($"=== STORAGE_EMULATOR_HOST={host}, EmulatorDetection.EmulatorOnly");
    StorageClient client;
    try
    {
        client = new StorageClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly, HttpClientFactory = new StubbedGcsFactory(tracking, stub) }.Build();
    }
    catch (Exception e)
    {
        Console.WriteLine($"  Build threw {e.GetType().Name}: {e.Message}");
        continue;
    }
    Console.WriteLine($"  client base: {client.Service.BaseUri}");
    foreach (var (name, call) in new (string, Action)[]
    {
        ("GetBucket", () => client.GetBucket("orders")),
        ("UploadObject", () => client.UploadObject("orders", "order-1.json", "application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}")))),
        ("DownloadObject", () => client.DownloadObject("orders", "order-1.json", new MemoryStream())),
        ("DeleteBucket", () => client.DeleteBucket("orders")),
    })
    {
        var before = stub.Sent.Count;
        try { call(); } catch (Exception e) { Console.WriteLine($"  {name} threw {e.GetType().Name}"); }
        foreach (var (method, uri, logged) in stub.Sent.Skip(before))
            Console.WriteLine($"  {name,-15} {method,-6} {uri.GetLeftPart(UriPartial.Authority),-24} {uri.PathAndQuery,-62} logged: {logged}");
    }
}

class StubbedGcsFactory(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new CloudStorageTrackingMessageHandler(options, inner);
}

// The tracking handler logs a request before calling its inner handler, so the stub knows whether, and as what, the
// call it answers was recorded.
class Stub(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
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
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GcsBody, Encoding.UTF8, "application/json") };
        if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
            response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
        return Task.FromResult(response);
    }
}
