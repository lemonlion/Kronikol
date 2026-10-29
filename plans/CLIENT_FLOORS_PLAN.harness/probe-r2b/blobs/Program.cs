// R2b's fourth test for Kronikol.Extensions.BlobStorage (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Azure.Storage.Blobs client, its options given WithTestTracking, against Azurite, on the floor the extension asks for
// (no property) or on the newest 12.x (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- <Azurite's blob endpoint, e.g. http://127.0.0.1:20000/devstoreaccount1>
using System.Reflection;
using Azure.Storage;
using Azure.Storage.Blobs;
using Kronikol.Extensions.BlobStorage;
using Kronikol.Tracking;

const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
var endpoint = new Uri(args.Length > 0 ? args[0] : "http://127.0.0.1:20000/devstoreaccount1");
var version = typeof(BlobServiceClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Azure.Storage.Blobs {version.Split('+')[0]} | Azurite at {endpoint}");

var options = new BlobClientOptions().WithTestTracking(new BlobTrackingMessageHandlerOptions
{
    CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
});
var service = new BlobServiceClient(endpoint, new StorageSharedKeyCredential("devstoreaccount1", AccountKey), options);
var container = service.GetBlobContainerClient("probe-" + version.Split('+')[0].Replace('.', '-'));
var blob = container.GetBlobClient("a.txt");

await Step("create a container", async () => { await container.CreateIfNotExistsAsync(); return "created"; });
await Step("upload a blob", async () => { await blob.UploadAsync(BinaryData.FromString("hello"), overwrite: true); return "uploaded"; });
await Step("download it", async () => (await blob.DownloadContentAsync()).Value.Content.ToString());
await Step("list the container", async () =>
{
    var names = new List<string>();
    await foreach (var item in container.GetBlobsAsync())
        names.Add(item.Name);
    return string.Join(",", names);
});
await Step("delete the blob", async () => { await blob.DeleteAsync(); return "deleted"; });
await Step("delete the container", async () => { await container.DeleteAsync(); return "deleted"; });

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri.AbsolutePath} {log.StatusCode?.Value} ({log.ServiceName})");

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}
