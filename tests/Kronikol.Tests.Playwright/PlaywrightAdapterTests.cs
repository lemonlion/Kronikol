using System.Net;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Playwright;
using Kronikol.Tracking;

namespace Kronikol.Tests.Playwright;

public class TestTrackingIdentityTests
{
    [Fact]
    public void Create_mints_an_identity_whose_test_id_defaults_to_the_w3c_trace_id()
    {
        var identity = TestTrackingIdentity.Create("overview › renders");

        Assert.Equal("overview › renders", identity.TestName);
        Assert.Equal(identity.TraceId.ToString("N"), identity.TestId);
        Assert.Equal(32, identity.TestId.Length);
        Assert.Equal(TestTrackingIdentity.DefaultCallerName, identity.CallerName);
    }

    [Fact]
    public void Headers_carry_the_four_tracking_headers_and_a_traceparent_rooted_at_the_trace_id()
    {
        var identity = TestTrackingIdentity.Create("overview › renders", "explicit-id", "SPA");

        var headers = identity.ToHeaders();

        Assert.Equal("overview ? renders", headers[TestTrackingHttpHeaders.CurrentTestNameHeader]); // ISO-8859-1 safe
        Assert.Equal("explicit-id", headers[TestTrackingHttpHeaders.CurrentTestIdHeader]);
        Assert.Equal("SPA", headers[TestTrackingHttpHeaders.CallerNameHeader]);
        Assert.Equal(identity.TraceId.ToString(), headers[TestTrackingHttpHeaders.TraceIdHeader]);
        var traceparent = TraceParent.TryParse(headers["traceparent"]);
        Assert.NotNull(traceparent);
        Assert.Equal(identity.W3CTraceId, traceparent!.TraceId);
        Assert.True(traceparent.Sampled);
        Assert.Equal(5, headers.Count);
    }

    [Fact]
    public void Traceparent_can_be_omitted_and_long_values_are_bounded()
    {
        var identity = TestTrackingIdentity.Create(new string('n', 1000)) with { IncludeTraceparent = false };
        var headers = identity.ToHeaders();
        Assert.False(headers.ContainsKey("traceparent"));
        Assert.Equal(512, headers[TestTrackingHttpHeaders.CurrentTestNameHeader].Length);
    }

    [Fact]
    public void BeginScope_opens_the_matching_in_process_identity_scope()
    {
        var identity = TestTrackingIdentity.Create("scoped", "scope-id");
        Assert.Null(TestIdentityScope.Current);
        using (identity.BeginScope())
            Assert.Equal(("scoped", "scope-id"), TestIdentityScope.Current);
        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public void FromCurrentScope_reads_the_ambient_identity()
    {
        using (TestIdentityScope.Begin("ambient", "ambient-id"))
        {
            var identity = TestTrackingIdentity.FromCurrentScope();
            Assert.Equal("ambient", identity.TestName);
            Assert.Equal("ambient-id", identity.TestId);
        }

        Assert.Throws<InvalidOperationException>(() => TestTrackingIdentity.FromCurrentScope());
    }

    [Fact]
    public void Merge_lets_identity_win_over_additional_headers_but_keeps_the_rest()
    {
        var identity = TestTrackingIdentity.Create("t", "id");
        var merged = PlaywrightTestTrackingExtensions.Merge(identity, new Dictionary<string, string>
        {
            ["X-Custom"] = "keep",
            [TestTrackingHttpHeaders.CurrentTestIdHeader] = "overridden",
        });
        Assert.Equal("keep", merged["X-Custom"]);
        Assert.Equal("id", merged[TestTrackingHttpHeaders.CurrentTestIdHeader]);
    }
}

/// <summary>Real Chromium against a local listener: the headers must arrive on every request the page makes.</summary>
public class PlaywrightBrowserTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async ValueTask InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    private sealed class EchoServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        public readonly List<Dictionary<string, string>> Seen = [];
        public int Port { get; }

        public EchoServer()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var k in ctx.Request.Headers.AllKeys) if (k is not null) headers[k] = ctx.Request.Headers[k]!;
                    lock (Seen) Seen.Add(headers);
                    var body = System.Text.Encoding.UTF8.GetBytes(ctx.Request.Url!.AbsolutePath == "/"
                        ? "<html><body><script>fetch('/api/data').then(r=>r.text()).then(t=>{document.body.setAttribute('data-done',t)})</script></body></html>"
                        : "ok");
                    ctx.Response.ContentType = ctx.Request.Url.AbsolutePath == "/" ? "text/html" : "text/plain";
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body);
                    ctx.Response.Close();
                }
            });
        }

        public ValueTask DisposeAsync()
        {
            try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Tracked_context_stamps_the_headers_on_page_navigations_and_xhr()
    {
        await using var server = new EchoServer();
        var identity = TestTrackingIdentity.Create("playwright › headers arrive");
        await using var context = await _browser.NewTrackedContextAsync(identity, new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Custom"] = "kept" },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync($"http://localhost:{server.Port}/");
        await page.WaitForFunctionAsync("() => document.body.getAttribute('data-done') === 'ok'", null, new PageWaitForFunctionOptions { PollingInterval = 200, Timeout = 15000 });

        Assert.Equal(2, server.Seen.Count);
        foreach (var request in server.Seen)
        {
            Assert.Equal("playwright ? headers arrive", request[TestTrackingHttpHeaders.CurrentTestNameHeader]);
            Assert.Equal(identity.TestId, request[TestTrackingHttpHeaders.CurrentTestIdHeader]);
            Assert.Equal(TestTrackingIdentity.DefaultCallerName, request[TestTrackingHttpHeaders.CallerNameHeader]);
            Assert.Equal(identity.TraceId.ToString(), request[TestTrackingHttpHeaders.TraceIdHeader]);
            Assert.Equal(identity.W3CTraceId, TraceParent.TryParse(request["traceparent"])!.TraceId);
            Assert.Equal("kept", request["X-Custom"]);
        }
    }

    [Fact]
    public async Task UseTestTrackingAsync_on_an_existing_context_and_a_proxy_tap_attributes_the_browser_call()
    {
        await using var server = new EchoServer();
        var sink = new ListSink();
        var identity = TestTrackingIdentity.Create("playwright › through the tap");
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = FreePort(),
            ForwardBaseUri = new Uri($"http://localhost:{server.Port}"),
            CallerName = "Browser",
            ServiceName = "web",
            Sink = sink,
        });
        await tap.StartAsync();

        await using var context = await _browser.NewContextAsync();
        await context.UseTestTrackingAsync(identity);
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(tap.ListenUri, "/api/data").ToString());
        await tap.DisposeAsync();

        var request = sink.Logs.First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(identity.TestId, request.TestId);
        Assert.Equal("playwright ? through the tap", request.TestName);
        Assert.Equal("/api/data", request.Uri.PathAndQuery);
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class ListSink : IRequestResponseSink
    {
        public readonly List<RequestResponseLog> Logs = [];
        public void Log(RequestResponseLog log) { lock (Logs) Logs.Add(log); }
    }
}
