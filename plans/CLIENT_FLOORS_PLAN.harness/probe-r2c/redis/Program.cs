// R2c's fourth test for Kronikol.Extensions.Redis (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real StackExchange.Redis
// client, its database tracked through GetTrackedDatabase, against a real Redis, on the floor the extension asks for (no
// property) or on the newest 2.x (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- [<host:port>, default 127.0.0.1:26379]
using System.Reflection;
using Kronikol.Extensions.Redis;
using Kronikol.Tracking;
using StackExchange.Redis;

var endpoint = args.Length > 0 ? args[0] : "127.0.0.1:26379";
var version = typeof(ConnectionMultiplexer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# StackExchange.Redis {version.Split('+')[0]} | Redis at {endpoint}");

using var multiplexer = await ConnectionMultiplexer.ConnectAsync(endpoint);
var db = multiplexer.GetTrackedDatabase(new RedisTrackingDatabaseOptions { CurrentTestInfoFetcher = () => ("probe scenario", "probe-1") });
var key = "probe:" + version.Split('+')[0];

await Step("set a string", async () => (await db.StringSetAsync(key, "hello")).ToString());
await Step("get it", async () => (await db.StringGetAsync(key)).ToString());
await Step("get it with its expiry", async () => (await db.StringGetWithExpiryAsync(key)).Value.ToString());
await Step("increment a counter", async () => (await db.StringIncrementAsync(key + ":n")).ToString());
await Step("push to a list", async () => (await db.ListRightPushAsync(key + ":l", "a")).ToString());
await Step("set and get a hash field", async () =>
{
    await db.HashSetAsync(key + ":h", "f", "v");
    return (await db.HashGetAsync(key + ":h", "f")).ToString();
});
await Step("expire it", async () => (await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1))).ToString());
await Step("delete the keys", async () => (await db.KeyDeleteAsync([key, key + ":n", key + ":l", key + ":h"])).ToString());

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} ({log.ServiceName})");

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}");
    }
}
