// FAILED_CALLS_PLAN.md: the cases the first probe (failed-calls.cs) read from the source only. Each drives a shipped tracker
// from the published 5.31.1 packages and prints the records it wrote, by test, and what the caller saw.
//   1. DeferredLogFlushHandler over a send that throws, then a send that does not, under another test (F28).
//   2. EF Core: a command whose token is cancelled once the request is logged (F8): which interceptor method EF Core calls.
//   3. Sqlite: a reader whose second row fails (F10).
//   4. TrackingProxy: an IAsyncEnumerable that throws partway (F24).
// Run from the harness directory: dotnet run --file probes/more-failed-calls.cs > results/more-failed-calls-5.31.1.txt 2>&1
#:package Kronikol@5.31.1
#:package Kronikol.Extensions.EfCore.Relational@5.31.1
#:package Kronikol.Extensions.Sqlite@5.31.1
#:package Microsoft.EntityFrameworkCore.Sqlite@10.0.11
#:property PublishAot=false

using System.Data.Common;
using System.Net;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Extensions.Sqlite;
using Kronikol.Tracking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var currentTest = "none";
(string Name, string Id) Current() => (currentTest, currentTest);

void Print(string test)
{
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == test).ToArray();
    Console.WriteLine($"  records under {test}: {logs.Length}");
    foreach (var l in logs)
        Console.WriteLine($"    {l.Type,-8} {l.Method.Value,-12} {l.Uri} status={l.StatusCode?.Value?.ToString() ?? "(none)"} " +
                          $"content={Short(l.Content)} error={Short(l.Error)} id={l.RequestResponseId.ToString()[..8]}");
}

static string Short(string? s) => s is null ? "(null)" : "\"" + (s.Length > 70 ? s[..70] + "..." : s).Replace("\n", " ") + "\"";

void Caught(Exception e) =>
    Console.WriteLine($"  caller caught {e.GetType().Name}: {e.Message.Split('\n')[0]}");

// 1. Deferred flush
Console.WriteLine("== 1. DeferredLogFlushHandler, a send that throws, then one that does not (F28)");
var work = TrackingProxy<IWork>.Create(new Work(), new TrackingProxyOptions { ServiceName = "Work", LogMode = TrackingLogMode.Deferred });
currentTest = "test-A";
using (var clientA = new HttpClient(new DeferredLogFlushHandler(Current) { InnerHandler = new AppHandler(work, throwIt: true) }))
{
    try { await clientA.GetAsync("http://app/a"); }
    catch (Exception e) { Caught(e); }
}
Console.WriteLine($"  pending after test-A's call: {PendingRequestResponseLogs.Count}");
Print("test-A");
currentTest = "test-B";
using (var clientB = new HttpClient(new DeferredLogFlushHandler(Current) { InnerHandler = new AppHandler(work, throwIt: false) }))
{
    var response = await clientB.GetAsync("http://app/b");
    Console.WriteLine($"  test-B's call answered {(int)response.StatusCode}");
}
Console.WriteLine($"  pending after test-B's call: {PendingRequestResponseLogs.Count}");
Print("test-A");
Print("test-B");

// 2. EF Core cancel
Console.WriteLine("== 2. EF Core: the token cancelled after the interceptor logged the request (F8)");
using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    connection.Open();
    var cts = new CancellationTokenSource();
    var spy = new CancelOnExecuting(cts);
    var options = new DbContextOptionsBuilder<Db>()
        .UseSqlite(connection)
        .AddInterceptors(new SqlTrackingInterceptor(new SqlTrackingInterceptorOptions { ServiceName = "SQLite", CurrentTestInfoFetcher = Current }), spy)
        .Options;
    currentTest = "setup";
    using var db = new Db(options);
    db.Database.EnsureCreated();
    currentTest = "ef-cancel";
    spy.Armed = true;
    try { await db.Rows.ToListAsync(cts.Token); }
    catch (Exception e) { Caught(e); }
    Console.WriteLine($"  EF Core called: {string.Join(", ", spy.Called)}");
    Print("ef-cancel");
}

// 3. A reader whose second row fails
Console.WriteLine("== 3. Sqlite: a reader whose second row fails (F10)");
currentTest = "partial-reader";
using (var tracked = new TrackingSqliteConnection(new SqliteConnection("Data Source=:memory:"), new SqliteTrackingOptions { ServiceName = "SQLite", CurrentTestInfoFetcher = () => Current() }))
{
    tracked.Open();
    using var command = tracked.CreateCommand();
    command.CommandText = "SELECT 1 UNION ALL SELECT abs(-9223372036854775808)";
    try
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
            Console.WriteLine($"  row: {reader.GetValue(0)}");
    }
    catch (Exception e) { Caught(e); }
}
Print("partial-reader");

// 4. A TrackingProxy stream that throws partway
Console.WriteLine("== 4. TrackingProxy: an IAsyncEnumerable that throws after one item (F24)");
currentTest = "proxy-stream";
var streamer = TrackingProxy<IStreamer>.Create(new Streamer(), new TrackingProxyOptions { ServiceName = "Streamer", CurrentTestInfoFetcher = Current });
try
{
    await foreach (var item in streamer.Items())
        Console.WriteLine($"  item: {item}");
}
catch (Exception e) { Caught(e); }
Print("proxy-stream");

public interface IWork { int Do(int x); }
public sealed class Work : IWork { public int Do(int x) => x + 1; }

public sealed class AppHandler(IWork work, bool throwIt) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        work.Do(1);
        if (throwIt)
            throw new InvalidOperationException("the app threw");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

public interface IStreamer { IAsyncEnumerable<int> Items(); }
public sealed class Streamer : IStreamer
{
    public async IAsyncEnumerable<int> Items()
    {
        yield return 1;
        await Task.Yield();
        throw new InvalidOperationException("the stream broke");
    }
}

public sealed class Db(DbContextOptions<Db> options) : DbContext(options)
{
    public DbSet<Row> Rows => Set<Row>();
}
public sealed class Row { public int Id { get; set; } }

// Registered after the tracking interceptor, so the request is logged before the token is cancelled.
public sealed class CancelOnExecuting(CancellationTokenSource cts) : DbCommandInterceptor
{
    public bool Armed { get; set; }
    public List<string> Called { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Armed) cts.Cancel();
        return ValueTask.FromResult(result);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Armed) Called.Add($"CommandFailedAsync({eventData.Exception.GetType().Name})");
        return Task.CompletedTask;
    }

    public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Armed) Called.Add("CommandCanceledAsync");
        return Task.CompletedTask;
    }
}
