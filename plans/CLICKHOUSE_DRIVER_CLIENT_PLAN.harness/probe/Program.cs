using ClickHouse.Driver.ADO;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Extensions.ClickHouse.Driver;
using Kronikol.Tracking;

const string cs = "Host=localhost;Port=8123;Username=default;Password=probe126";
var ds = new ClickHouseDataSource(cs);
var options = new ClickHouseTrackingOptions { CurrentTestInfoFetcher = () => ("probe", "probe-1") };
await using var tracked = ds.CreateConnection().WithClickHouseDriverTestTracking(options);
await tracked.OpenAsync();
var client = ds.GetClient();

async Task Run(System.Data.Common.DbConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
async Task<object?> Scalar(System.Data.Common.DbConnection c, string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync(); }

// 1. The issue's sequence
await Run(tracked, "DROP TABLE IF EXISTS orders");
var before = RequestResponseLogger.RequestAndResponseLogs.Length;
await Run(tracked, "CREATE TABLE orders (id UInt32, item String) ENGINE = MergeTree ORDER BY id");
var inserted = await client.InsertBinaryAsync("orders", new[] { "id", "item" }, new[] { new object[] { 1u, "toast" }, new object[] { 2u, "eggs" } });
var deleted = await client.ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1");
var count = await Scalar(tracked, "SELECT count() FROM orders");
Console.WriteLine($"InsertBinaryAsync returned {inserted}; client ExecuteNonQueryAsync(DELETE) returned {deleted}; SELECT count() = {count}");
void Dump(string label, int from)
{
    var reqs = RequestResponseLogger.RequestAndResponseLogs.Skip(from).Where(l => l.Type == RequestResponseType.Request).ToArray();
    Console.WriteLine($"{label}: {reqs.Length} calls recorded");
    foreach (var r in reqs) Console.WriteLine($"  [{r.ServiceName}/{r.DependencyCategory}] {r.Method.Value} {r.Uri} | {(r.Content ?? "").Split('\n')[0]}");
}
Dump("Issue sequence", before);

// 2. Client return values for statements whose count the server reports only in X-ClickHouse-Summary
var insertVals = await client.ExecuteNonQueryAsync("INSERT INTO orders VALUES (3, 'beans'), (4, 'tea')");
Console.WriteLine($"client ExecuteNonQueryAsync(INSERT 2 rows VALUES) returned {insertVals}");

// 3. A caller reusing one object[] for every row
await Run(tracked, "TRUNCATE TABLE orders");
IEnumerable<object[]> Reused() { var buf = new object[2]; for (uint i = 10; i < 13; i++) { buf[0] = i; buf[1] = $"r{i}"; yield return buf; } }
var reusedCount = await client.InsertBinaryAsync("orders", new[] { "id", "item" }, Reused());
var ids = await Scalar(tracked, "SELECT arrayStringConcat(groupArray(toString(id)), ',') FROM (SELECT id FROM orders ORDER BY id)");
Console.WriteLine($"reused-buffer insert returned {reusedCount}; ids stored: {ids}");

// 4. Workaround today: Kronikol's HTTP handler inside the data source's HttpClient
var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { CurrentTestInfoFetcher = () => ("probe", "probe-2"), CallerName = "Probe" }) { InnerHandler = new HttpClientHandler() };
using var httpDs = new ClickHouseDataSource(cs, new HttpClient(handler));
var mark = RequestResponseLogger.RequestAndResponseLogs.Length;
await httpDs.GetClient().InsertBinaryAsync("orders", new[] { "id", "item" }, new[] { new object[] { 20u, "jam" } });
await httpDs.GetClient().ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 20");
Dump("HTTP handler workaround", mark);
await Run(tracked, "DROP TABLE orders");

// 5. A statement the server rejects, through the tracked ADO.NET connection
var failMark = RequestResponseLogger.RequestAndResponseLogs.Length;
try { await Run(tracked, "SELECT * FROM no_such_table_126"); } catch (Exception e) { Console.WriteLine($"server rejected it: {e.GetType().Name}"); }
var after = RequestResponseLogger.RequestAndResponseLogs.Skip(failMark).ToArray();
Console.WriteLine($"failed statement: {after.Count(l => l.Type == RequestResponseType.Request)} request(s), {after.Count(l => l.Type == RequestResponseType.Response)} response(s) recorded");
