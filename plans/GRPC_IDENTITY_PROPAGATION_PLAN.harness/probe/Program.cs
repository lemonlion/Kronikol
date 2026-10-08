// Issue #134 probe: test --HTTP--> host A --gRPC--> host B --HTTP--> stub C, all in one process on TestServer,
// as the issue's suite runs (WebApplicationFactory hosts; TestServer does not flow the test's AsyncLocal).
// Usage: dotnet run -- <probe> [variant]
//   attribution control|workaround|noaccessor   P1/P6: is B's call to C attributed to the scenario?
//   attribution direct                          P1b: test -> B over gRPC, no host A (a single gRPC-fronted service)
//   attribution httpchain                       P8: the same chain with HTTP, not gRPC, between A and B
//   attribution httpchain-forward               P8b: as httpchain, with HeadersToForward naming the two identity headers
//   attribution httpchain-noaccessor            P8c: as httpchain, with A's handler built without an accessor
//   metadata                                    P2/P3: does the interceptor write into the caller's Metadata?
//   activity [recording]                        P4/P5: Activity.Current after an awaited unary call; flags B sees
//                                               (recording: a second listener records every span;
//                                                parentbased: it follows the parent's flag, OTel's default sampler)
//   accessor                                    P7: HasHttpContextAccessor as the registry reads it
//   nonascii                                    P10: identity headers with non-ASCII test names over real sockets
//   streaming                                   P9: what a server stream that fails after one reply is recorded as
using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Kronikol.Constants;
using Kronikol.Extensions.Grpc;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Probe.Hop;

var probe = args.ElementAtOrDefault(0) ?? "attribution";
var variant = args.ElementAtOrDefault(1) ?? "control";
var version = typeof(GrpcTrackingInterceptor).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
Console.WriteLine($"kronikol {version} probe={probe} variant={variant}");

RequestResponseLogger.CaptureBackground = true; // keep unattributed calls, so "dropped" shows as "unknown"

// Stub C: the downstream HTTP dependency B calls while serving the gRPC call.
var cApp = Host("C", b => { }, app => app.MapGet("/stub", () => "ok"));
await cApp.StartAsync();
var cServer = cApp.GetTestServer();

// Host B: a gRPC service that calls C through Kronikol's HTTP handler, with the propagation middleware.
var bApp = Host("B", b =>
{
    b.Services.AddGrpc();
    b.Services.AddHttpContextAccessor();
    b.Services.AddTestTrackingContextPropagation();
    b.Services.AddSingleton(sp => new HttpClient(new TestTrackingMessageHandler(
        new TestTrackingMessageHandlerOptions { CallerName = "Service B", FixedNameForReceivingService = "Stub C" },
        sp.GetRequiredService<IHttpContextAccessor>()) { InnerHandler = cServer.CreateHandler() })
        { BaseAddress = cServer.BaseAddress });
}, app =>
{
    app.MapGrpcService<HopService>();
    app.MapGet("/hop", async (HttpClient toC, HttpContext http) =>
    {
        await toC.GetStringAsync("/stub");
        return $"{http.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader]}|{http.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader]}|{http.Request.Headers["traceparent"]}";
    });
});
await bApp.StartAsync();
var bServer = bApp.GetTestServer();

switch (probe)
{
    case "attribution": await Attribution(); break;
    case "metadata": await MetadataProbe(); break;
    case "activity": await ActivityProbe(); break;
    case "accessor": AccessorProbe(); break;
    case "streaming": await StreamingProbe(); break;
    case "nonascii" when variant == "propagated": await PropagatedNonAsciiProbe(); break;
    case "nonascii": await NonAsciiProbe(); break;
    default: Console.WriteLine("unknown probe"); break;
}
return;

async Task Attribution()
{
    if (variant == "direct")
    {
        // One gRPC-fronted service called straight from the test, the CreateTestTrackingGrpcClient shape.
        using var scope = TestIdentityScope.Begin("Scenario one", "id-1");
        var reply = await DirectClient().CallAsync(new HopRequest { Text = "x" });
        Console.WriteLine($"B saw (test id|test name|traceparents): {reply.SeenTestId}|{reply.SeenTestName}|{reply.SeenTraceparents}");
        await Task.Delay(200);
        foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Type == RequestResponseType.Request))
            Console.WriteLine($"  {log.CallerName,-10} -> {log.ServiceName,-10} test={log.TestId,-8} source={log.AttributionSource}");
        return;
    }

    // Host A: an HTTP endpoint that calls B over gRPC. Its client is built as the issue's suite builds it.
    var aApp = Host("A", b =>
    {
        b.Services.AddHttpContextAccessor();
        b.Services.AddTestTrackingContextPropagation();
        b.Services.AddSingleton(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            var options = new GrpcTrackingOptions { ServiceName = "Service B", CallerName = "Service A" };
            var channel = GrpcChannel.ForAddress(bServer.BaseAddress,
                new GrpcChannelOptions { HttpHandler = new GrpcResponseVersionHandler(bServer.CreateHandler()) });
            CallInvoker invoker = variant switch
            {
                // The interceptor with no accessor and no fetcher, as GrpcTrackingChannel.Create leaves it.
                "noaccessor" => channel.Intercept(new GrpcTrackingInterceptor(options)),
                // The issue's workaround: a second interceptor that adds the two identity headers.
                "workaround" => channel.Intercept(new GrpcTrackingInterceptor(options, accessor))
                    .Intercept(new IdentityHeaderInterceptor(accessor)),
                _ => channel.Intercept(new GrpcTrackingInterceptor(options, accessor)),
            };
            return new Hop.HopClient(invoker);
        });
        // The httpchain variant: A calls B over HTTP through Kronikol's handler, with A's accessor.
        b.Services.AddSingleton(sp => new HttpClient(new TestTrackingMessageHandler(
            new TestTrackingMessageHandlerOptions
            {
                CallerName = "Service A", FixedNameForReceivingService = "Service B",
                // httpchain-forward: the existing opt-in, copying the incoming identity headers onto the outgoing call
                HeadersToForward = variant == "httpchain-forward"
                    ? [TestTrackingHttpHeaders.CurrentTestNameHeader, TestTrackingHttpHeaders.CurrentTestIdHeader]
                    : [],
            },
            // httpchain-noaccessor: A's handler built without the accessor, as a hand-built handler can be
            variant == "httpchain-noaccessor" ? null : sp.GetRequiredService<IHttpContextAccessor>())
            { InnerHandler = bServer.CreateHandler() })
            { BaseAddress = bServer.BaseAddress });
    }, app => app.MapGet("/start", async (Hop.HopClient client, HttpClient toB) =>
    {
        if (variant.StartsWith("httpchain"))
            return await toB.GetStringAsync("/hop");
        var reply = await client.CallAsync(new HopRequest { Text = "x" });
        return $"{reply.SeenTestId}|{reply.SeenTestName}|{reply.SeenTraceparents}";
    }));
    await aApp.StartAsync();
    var aServer = aApp.GetTestServer();

    var test = new HttpClient(new TestTrackingMessageHandler(
        new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test", FixedNameForReceivingService = "Service A",
            CurrentTestInfoFetcher = () => ("Scenario one", "id-1"),
        }) { InnerHandler = aServer.CreateHandler() }) { BaseAddress = aServer.BaseAddress };

    var body = await test.GetStringAsync("/start");
    Console.WriteLine($"B saw (test id|test name|traceparents): {body}");
    await Task.Delay(200);
    foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Type == RequestResponseType.Request))
        Console.WriteLine($"  {log.CallerName,-10} -> {log.ServiceName,-10} test={log.TestId,-8} source={log.AttributionSource}");
}

async Task MetadataProbe()
{
    var client = DirectClient();
    using var scope = TestIdentityScope.Begin("Scenario one", "id-1");

    var shared = new Metadata { { "x-auth", "token" } };
    for (var i = 1; i <= 3; i++)
    {
        var reply = await client.CallAsync(new HopRequest(), new CallOptions(headers: shared));
        var logged = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.Type == RequestResponseType.Request && l.ServiceName == "Service B")
            .Last().Headers.Count(h => h.Key == "traceparent");
        var sent = reply.SeenTraceparents.Split(" ; ", StringSplitOptions.RemoveEmptyEntries).Length;
        Console.WriteLine($"P2 after call {i}: shared Metadata holds {shared.Count} entries, " +
                          $"{shared.Count(e => e.Key == "traceparent")} traceparent; B received {sent} traceparent; " +
                          $"the call logged {logged} traceparent among its own request headers");
        Console.WriteLine($"    B's traceparent value(s): {reply.SeenTraceparents}");
        Console.WriteLine($"    B's server span: {reply.ServerActivityFlags}");
    }

    try
    {
        await client.CallAsync(new HopRequest(), new CallOptions(headers: Metadata.Empty));
        Console.WriteLine("P3 Metadata.Empty: call succeeded");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"P3 Metadata.Empty: {ex.GetType().Name}: {ex.Message}");
    }

    var concurrentShared = new Metadata { { "x-auth", "token" } };
    var errors = 0;
    await Task.WhenAll(Enumerable.Range(0, 64).Select(async _ =>
    {
        try { await client.CallAsync(new HopRequest(), new CallOptions(headers: concurrentShared)); }
        catch { Interlocked.Increment(ref errors); }
    }));
    Console.WriteLine($"P2b 64 concurrent calls on one Metadata: {concurrentShared.Count} entries afterwards, {errors} calls threw");
}

async Task ActivityProbe()
{
    var client = DirectClient();
    using var scope = TestIdentityScope.Begin("Scenario one", "id-1");
    // variant "recording": a second listener that records every span, as an OpenTelemetry SDK with AlwaysOn would
    using var recording = variant switch
    {
        "recording" => new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        },
        // parentbased: OpenTelemetry's default, ParentBased(AlwaysOn): a root is recorded, a child follows its parent's flag
        "parentbased" => new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> o) =>
                o.Parent == default || o.Parent.TraceFlags.HasFlag(ActivityTraceFlags.Recorded)
                    ? ActivitySamplingResult.AllDataAndRecorded
                    : ActivitySamplingResult.PropagationData,
        },
        _ => null,
    };
    if (recording is not null) ActivitySource.AddActivityListener(recording);
    using var root = new ActivitySource("Probe.Test").StartActivity("test");
    Console.WriteLine($"P4 before: Activity.Current = {Activity.Current?.OperationName ?? "null"} span {Activity.Current?.SpanId}");
    var first = await client.CallAsync(new HopRequest());
    Console.WriteLine($"P4 after call 1: Activity.Current = {Activity.Current?.OperationName ?? "null"} span {Activity.Current?.SpanId}");
    Console.WriteLine($"P5b the interceptor's own activity: Recorded={Activity.Current?.Recorded} ActivityTraceFlags={Activity.Current?.ActivityTraceFlags}");
    var second = await client.CallAsync(new HopRequest());
    Console.WriteLine($"P4 after call 2: Activity.Current = {Activity.Current?.OperationName ?? "null"}");
    Console.WriteLine($"P5 call 1 traceparent(s) at B: {first.SeenTraceparents}; B's server activity: {first.ServerActivityFlags}");
    Console.WriteLine($"P5 call 2 traceparent(s) at B: {second.SeenTraceparents}; B's server activity: {second.ServerActivityFlags}");
}

async Task StreamingProbe()
{
    var client = DirectClient();
    using var scope = TestIdentityScope.Begin("Scenario one", "id-1");
    var received = 0;
    string outcome;
    try
    {
        using var call = client.Stream(new HopRequest());
        await foreach (var _ in call.ResponseStream.ReadAllAsync()) received++;
        outcome = "completed";
    }
    catch (RpcException ex) { outcome = $"RpcException {ex.StatusCode}"; }
    Console.WriteLine($"P9 server stream: client received {received} message(s), then {outcome}");
    foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Where(l => l.ServiceName == "Service B"))
        Console.WriteLine($"  {log.Type,-8} status={(log.StatusCode is null ? "-" : log.StatusCode.Value?.ToString())} content={(log.Content ?? "null")}");
}

async Task NonAsciiProbe()
{
    // A real socket: Kestrel, one HTTP/1.1 endpoint and one h2c endpoint for gRPC. TestServer accepts anything.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(HopService).Assembly.GetName().Name });
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.Listen(System.Net.IPAddress.Loopback, 0, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
        k.Listen(System.Net.IPAddress.Loopback, 0, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    });
    builder.Services.AddGrpc();
    builder.Services.AddSingleton(new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://stub") });
    var app = builder.Build();
    app.MapGrpcService<HopService>();
    app.MapGet("/stub", (HttpContext http) => $"name={http.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader]}");
    await app.StartAsync();
    var addresses = app.Urls.ToArray();
    var http1 = addresses[0]; var h2c = addresses[1];

    foreach (var name in new[] { "Place an order", "Café order", "Order a coffee ☕", "Theory(text: \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"···)", "Theory(a" + (char)10 + "b)" })
    {
        var testClient = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test", FixedNameForReceivingService = "Kestrel", CurrentTestInfoFetcher = () => (name, "id-2"),
        }) { InnerHandler = new SocketsHttpHandler() }) { BaseAddress = new Uri(http1) };
        string httpOutcome;
        try { httpOutcome = "ok, server saw " + await testClient.GetStringAsync("/stub"); }
        catch (Exception ex) { httpOutcome = $"{ex.GetType().Name}: {ex.Message}"; }

        using var scope = TestIdentityScope.Begin(name, "id-2");
        var channel = GrpcChannel.ForAddress(h2c);
        var client = new Hop.HopClient(channel.Intercept(new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Kestrel", CallerName = "Test" }))
            .Intercept(new IdentityHeaderInterceptor(new HttpContextAccessor())));
        string grpcOutcome;
        try { var r = await client.CallAsync(new HopRequest()); grpcOutcome = $"ok, server saw name={r.SeenTestName}"; }
        catch (Exception ex) { grpcOutcome = $"{ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}"; }
        // The same two calls in memory, through TestServer (host B), which never encodes headers
        var memHttp = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test", FixedNameForReceivingService = "Service B", CurrentTestInfoFetcher = () => (name, "id-2"),
        }) { InnerHandler = bServer.CreateHandler() }) { BaseAddress = bServer.BaseAddress };
        string memHttpOutcome;
        try { memHttpOutcome = "ok, B saw " + await memHttp.GetStringAsync("/hop"); }
        catch (Exception ex) { memHttpOutcome = $"{ex.GetType().Name}: {ex.Message}"; }
        var memChannel = GrpcChannel.ForAddress(bServer.BaseAddress,
            new GrpcChannelOptions { HttpHandler = new GrpcResponseVersionHandler(bServer.CreateHandler()) });
        var memClient = new Hop.HopClient(memChannel.Intercept(new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Service B", CallerName = "Test" }))
            .Intercept(new IdentityHeaderInterceptor(new HttpContextAccessor())));
        string memGrpcOutcome;
        try { var r = await memClient.CallAsync(new HopRequest()); memGrpcOutcome = $"ok, B saw name={r.SeenTestName}"; }
        catch (Exception ex) { memGrpcOutcome = $"{ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}"; }

        // The same over the sockets, with the values encoded by section 4.1's rule and decoded where they arrive
        var encodedHttp = new HttpClient(new SocketsHttpHandler()) { BaseAddress = new Uri(http1) };
        var encodedRequest = new HttpRequestMessage(HttpMethod.Get, "/stub");
        encodedRequest.Headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, Section41.Encode(name));
        encodedRequest.Headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, Section41.Encode("id-2"));
        string encodedHttpOutcome;
        try
        {
            var raw = (await (await encodedHttp.SendAsync(encodedRequest)).Content.ReadAsStringAsync())["name=".Length..];
            encodedHttpOutcome = Section41.Decode(raw) == name ? "ok, the exact name after decoding" : $"ok, but decoded to {Section41.Decode(raw)}";
        }
        catch (Exception ex) { encodedHttpOutcome = $"{ex.GetType().Name}: {ex.Message}"; }
        var encodedGrpc = new Hop.HopClient(GrpcChannel.ForAddress(h2c)
            .Intercept(new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Kestrel", CallerName = "Test" }))
            .Intercept(new IdentityHeaderInterceptor(new HttpContextAccessor(), encode: true)));
        string encodedGrpcOutcome;
        try
        {
            var r = await encodedGrpc.CallAsync(new HopRequest());
            encodedGrpcOutcome = Section41.Decode(r.SeenTestName) == name ? "ok, the exact name after decoding" : $"ok, but decoded to {Section41.Decode(r.SeenTestName)}";
        }
        catch (Exception ex) { encodedGrpcOutcome = $"{ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}"; }

        Console.WriteLine($"P10 name \"{name.Replace(((char)10).ToString(), "<LF>")}\"");
        Console.WriteLine($"  encoded (section 4.1), HTTP over a socket: {encodedHttpOutcome}");
        Console.WriteLine($"  encoded (section 4.1), gRPC over h2c: {encodedGrpcOutcome}");
        Console.WriteLine($"  HTTP handler over a socket: {httpOutcome}");
        Console.WriteLine($"  gRPC identity metadata over h2c: {grpcOutcome}");
        Console.WriteLine($"  HTTP handler in memory (TestServer): {memHttpOutcome}");
        Console.WriteLine($"  gRPC identity metadata in memory (TestServer): {memGrpcOutcome}");
    }
    await app.StopAsync();
}

// R1's acceptance (plan section 6.6): the five names over a real h2c socket through the interceptor alone, no
// workaround. A Kestrel host that registers AddTestTrackingContextPropagation() calls stub C through Kronikol's HTTP
// handler with its own accessor; the line reads that call from the log: whose it is, and through what.
async Task PropagatedNonAsciiProbe()
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(HopService).Assembly.GetName().Name });
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(k =>
        k.Listen(System.Net.IPAddress.Loopback, 0, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2));
    builder.Services.AddGrpc();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddTestTrackingContextPropagation();
    builder.Services.AddSingleton(sp => new HttpClient(new TestTrackingMessageHandler(
        new TestTrackingMessageHandlerOptions { CallerName = "Kestrel B", FixedNameForReceivingService = "Stub C via Kestrel" },
        sp.GetRequiredService<IHttpContextAccessor>()) { InnerHandler = cServer.CreateHandler() })
        { BaseAddress = cServer.BaseAddress });
    var app = builder.Build();
    app.MapGrpcService<HopService>();
    await app.StartAsync();
    var h2c = app.Urls.First();

    var n = 0;
    foreach (var name in new[] { "Place an order", "Café order", "Order a coffee ☕", "Theory(text: \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"···)", "Theory(a" + (char)10 + "b)" })
    {
        var id = $"id-p{++n}";
        var client = new Hop.HopClient(GrpcChannel.ForAddress(h2c).Intercept(new GrpcTrackingInterceptor(new GrpcTrackingOptions
        {
            ServiceName = "Kestrel B", CallerName = "Test", CurrentTestInfoFetcher = () => (name, id),
        })));
        string outcome;
        try { await client.CallAsync(new HopRequest()); outcome = "ok"; }
        catch (Exception ex) { outcome = $"{ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}"; }
        var bToC = RequestResponseLogger.RequestAndResponseLogs
            .LastOrDefault(l => l.ServiceName == "Stub C via Kestrel" && l.Type == RequestResponseType.Request && l.TestId == id);
        var seen = bToC is null
            ? "B's call to C: not under this test (" + (RequestResponseLogger.RequestAndResponseLogs
                .Count(l => l.ServiceName == "Stub C via Kestrel" && l.Type == RequestResponseType.Request && l.TestId != id) > 0 ? "recorded under another id" : "not recorded") + ")"
            : $"B's call to C: {bToC.TestId}, {bToC.AttributionSource}, name {(bToC.TestName == name ? "exact" : "differs: " + bToC.TestName)}";
        Console.WriteLine($"R1 name \"{name.Replace(((char)10).ToString(), "<LF>")}\": call {outcome}; {seen}");
    }
    await app.StopAsync();
}

void AccessorProbe()
{
    var accessor = new HttpContextAccessor();
    var interceptor = new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Probe" }, accessor);
    ITrackingComponent component = interceptor;
    Console.WriteLine($"P7 constructed with an accessor: HasHttpContextAccessor = {component.HasHttpContextAccessor}");
}

Hop.HopClient DirectClient()
{
    // The test-to-host direction: the interceptor on the test side, no accessor, identity from the fetcher.
    var channel = GrpcChannel.ForAddress(bServer.BaseAddress,
        new GrpcChannelOptions { HttpHandler = new GrpcResponseVersionHandler(bServer.CreateHandler()) });
    var options = new GrpcTrackingOptions
    {
        ServiceName = "Service B", CallerName = "Test",
        CurrentTestInfoFetcher = () => TestIdentityScope.Current ?? TestIdentityScope.UnknownIdentity,
    };
    return new Hop.HopClient(channel.Intercept(new GrpcTrackingInterceptor(options)));
}

static WebApplication Host(string name, Action<WebApplicationBuilder> configure, Action<WebApplication> map)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(HopService).Assembly.GetName().Name });
    builder.WebHost.UseTestServer();
    builder.Logging.ClearProviders();
    configure(builder);
    var app = builder.Build();
    map(app);
    return app;
}

/// <summary>Host B's service: reports what arrived and calls C while serving the call.</summary>
public class HopService(HttpClient toC) : Hop.HopBase
{
    // One reply, then the stream fails: what does the interceptor record for the call?
    public override async Task Stream(HopRequest request, IServerStreamWriter<HopReply> responseStream, ServerCallContext context)
    {
        await responseStream.WriteAsync(new HopReply { SeenTestId = "first" });
        throw new RpcException(new Status(StatusCode.NotFound, "gone after the first reply"));
    }

    public override async Task<HopReply> Call(HopRequest request, ServerCallContext context)
    {
        await toC.GetStringAsync("/stub");
        var headers = context.RequestHeaders;
        var activity = Activity.Current;
        return new HopReply
        {
            SeenTestId = headers.GetValue(TestTrackingHttpHeaders.CurrentTestIdHeader) ?? "(none)",
            SeenTestName = headers.GetValue(TestTrackingHttpHeaders.CurrentTestNameHeader) ?? "(none)",
            SeenTraceparents = string.Join(" ; ", headers.Where(h => h.Key == "traceparent").Select(h => h.Value)),
            ServerActivityFlags = activity is null ? "none" : $"{activity.OperationName} recorded={activity.Recorded} parent={activity.ParentId} trace={activity.TraceId} parentSpan={activity.ParentSpanId} format={activity.IdFormat}",
        };
    }
}

public class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") });
}

/// <summary>The plan's section 4.1 rule (the same as escape/esc.cs), to send what R0 and R1 would send.</summary>
public static class Section41
{
    private const string Prefix = "UTF-8''";

    public static string Encode(string v) =>
        v.All(c => c >= (char)0x20 && c <= (char)0x7E) && !v.StartsWith(Prefix, StringComparison.Ordinal)
        && (v.Length == 0 || (v[0] != ' ' && v[^1] != ' '))
            ? v
            : Prefix + Uri.EscapeDataString(v);

    public static string Decode(string v) =>
        v.StartsWith(Prefix, StringComparison.Ordinal) ? Uri.UnescapeDataString(v[Prefix.Length..]) : v;
}

/// <summary>The issue's workaround, as filed; with <paramref name="encode"/>, the values go through section 4.1's rule.</summary>
public class IdentityHeaderInterceptor(IHttpContextAccessor accessor, bool encode = false) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (TestInfoResolver.ResolveWithSource(accessor, (Func<(string, string)>?)null) is { IsAttributed: true } identity)
        {
            var headers = new Metadata();
            foreach (var entry in context.Options.Headers ?? []) headers.Add(entry);
            headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, encode ? Section41.Encode(identity.Name) : identity.Name);
            headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, encode ? Section41.Encode(identity.Id) : identity.Id);
            context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithHeaders(headers));
        }
        return continuation(request, context);
    }
}
