using System.Collections.Concurrent;
using System.Net;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Repro133;

public static class Probe
{
    public static readonly string Out = Path.Combine(AppContext.BaseDirectory, "probe.txt");

    public static void Write(string stage)
    {
        string fetcher;
        try { var (n, i) = CurrentTestInfo.Fetcher(); fetcher = $"({n}, {i})"; }
        catch (Exception e) { fetcher = $"throws {e.GetType().Name}"; }
        var resolved = TestInfoResolver.ResolveWithSource(null, CurrentTestInfo.Fetcher);
        var r = resolved is { } t ? $"({t.Name}, {t.Id}, {t.Source})" : "null";
        string track;
        if (Track.TestIdResolver is null) track = "<unset>";
        else { try { track = Track.TestIdResolver() ?? "null"; } catch (Exception e) { track = "throws " + e.GetType().Name; } }
        File.AppendAllText(Out, $"{stage}\n  fetcher:  {fetcher}\n  resolver: {r}\n  Track.TestIdResolver: {track}\n");
    }

    public static async Task Call(string label)
    {
        var handler = new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions { CallerName = "Tests", FixedNameForReceivingService = "Svc" })
        { InnerHandler = new Stub() };
        using var client = new HttpClient(handler);
        await client.GetAsync("http://svc.local/" + label);
    }

    public static void DumpLogs(string when)
    {
        var lines = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.Type == RequestResponseType.Request)
            .Select(l => $"    {l.Uri.AbsolutePath,-22} TestName={l.TestName} TestId={l.TestId} Source={l.AttributionSource} override={l.IsOverrideStart || l.IsActionStart}");
        File.AppendAllText(Out, $"-- request logs {when}:\n{string.Join("\n", lines)}\n-- headers the downstream service received:\n{string.Join("\n", Stub.Seen.Select(s => "    " + s))}\n");
    }
}

public class Stub : HttpMessageHandler
{
    public static readonly ConcurrentQueue<string> Seen = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var name = request.Headers.Where(h => h.Key.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
        Seen.Enqueue($"{request.RequestUri!.AbsolutePath}: {string.Join("; ", name)}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

[TestTracking]
public class A_Lifecycle : IAsyncLifetime, IDisposable
{
    public A_Lifecycle()
    {
        Probe.Write("test class constructor (xUnit2 runs it before TestTrackingAttribute.Before)");
        Probe.Write("constructor, a second time (a new id each call?)");
        using (TestIdentityScope.Begin("Scoped", "scoped-id"))
            Probe.Write("constructor, inside TestIdentityScope.Begin(\"Scoped\", \"scoped-id\")");
        Probe.Call("from-constructor").GetAwaiter().GetResult();
        TrackingDiagramOverride.StartAction();
    }

    public Task InitializeAsync() { Probe.Write("IAsyncLifetime.InitializeAsync"); return Task.CompletedTask; }

    [Fact]
    public async Task Test_one()
    {
        Probe.Write("test method body");
        await Probe.Call("from-test-method");

        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            var thread = new Thread(() => Probe.Write("thread started with no flowed context, GlobalFallback set (\"fallback-id\")"));
            using (ExecutionContext.SuppressFlow()) thread.Start();
            thread.Join();
        }
        finally { TestIdentityScope.ClearGlobalFallback(); }
    }

    public Task DisposeAsync() { Probe.Write("IAsyncLifetime.DisposeAsync"); return Task.CompletedTask; }

    public void Dispose()
    {
        Probe.Write("Dispose");
        Probe.DumpLogs("after the test");
    }
}
