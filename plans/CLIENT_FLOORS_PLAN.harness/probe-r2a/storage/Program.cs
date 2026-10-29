// R2a's fourth test for Kronikol.Extensions.CloudStorage (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Google.Cloud.Storage.V1 client, built with WithTestTracking, against fake-gcs-server, on the floor the extension asks
// for (no property) or on the newest 4.x (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- <base, e.g. http://127.0.0.1:4443/>
using System.Reflection;
using System.Text;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

var baseUri = args.Length > 0 ? args[0] : "http://127.0.0.1:4443/";
var version = typeof(StorageClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Google.Cloud.Storage.V1 {version.Split('+')[0]} | fake-gcs-server at {baseUri}");

var client = await new StorageClientBuilder
{
    BaseUri = baseUri.TrimEnd('/') + "/storage/v1/",
    UnauthenticatedAccess = true,
}.WithTestTracking(new CloudStorageTrackingMessageHandlerOptions
{
    CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
}).BuildAsync();

var bucket = "probe-" + version.Split('+')[0].Replace('.', '-');
await Step("create a bucket", () => client.CreateBucketAsync("probe", bucket));
await Step("upload an object", () => client.UploadObjectAsync(bucket, "a.txt", "text/plain", new MemoryStream(Encoding.UTF8.GetBytes("hello"))));
await Step("download it", async () =>
{
    var stream = new MemoryStream();
    await client.DownloadObjectAsync(bucket, "a.txt", stream);
    return Encoding.UTF8.GetString(stream.ToArray());
});
await Step("list the bucket", async () => string.Join(",", await client.ListObjectsAsync(bucket).Select(o => o.Name).ToListAsync()));
await Step("delete the object", async () => { await client.DeleteObjectAsync(bucket, "a.txt"); return "deleted"; });

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
