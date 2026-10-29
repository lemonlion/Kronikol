#:package Kronikol.Extensions.CloudStorage@5.12.0
#:package Google.Cloud.Storage.V1@4.16.0
#:property TargetFramework=net10.0
#:property PublishAot=false

// Which request does StorageClient.CopyObject send, and what do Detailed and Summarised record for it?
// Usage: dotnet run --file gcs-copy-object.cs

using System.Net;
using System.Text;
using Google.Apis.Http;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

foreach (var verbosity in new[] { CloudStorageTrackingVerbosity.Detailed, CloudStorageTrackingVerbosity.Summarised })
{
    var testId = Guid.NewGuid().ToString();
    var stub = new Stub();
    var options = new CloudStorageTrackingMessageHandlerOptions { Verbosity = verbosity, CurrentTestInfoFetcher = () => ("probe", testId) };
    var client = new StorageClientBuilder { UnauthenticatedAccess = true, HttpClientFactory = new Factory(options, stub) }.Build();
    try { client.CopyObject("archive", "a.json", "backup", "a.json"); } catch (Exception e) { Console.WriteLine($"  threw {e.GetType().Name}"); }
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList();
    Console.WriteLine($"{verbosity}: sent {string.Join(", ", stub.Sent)}; recorded {logs.Count}: {string.Join(", ", logs.Select(l => $"{l.Method.Value} {l.Uri.PathAndQuery}"))}");
}

class Factory(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler inner) : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new CloudStorageTrackingMessageHandler(options, inner);
}

class Stub : HttpMessageHandler
{
    public List<string> Sent { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent.Add($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"kind":"storage#rewriteResponse","done":true,"resource":{"name":"a.json","bucket":"backup"}}""", Encoding.UTF8, "application/json")
        });
    }
}
