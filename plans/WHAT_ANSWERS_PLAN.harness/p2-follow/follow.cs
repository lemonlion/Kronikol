#:sdk Microsoft.NET.Sdk.Web
#:package Kronikol@5.27.2
#:package Moq@4.21.0
#:package RichardSzalay.MockHttp@7.1.0
#:package JustEat.HttpClientInterception@5.1.4
#:property PublishAot=false
// P2 (#219): the walk the plan proposes (section 5.1), run over the issue's shapes and the doubles suites use, beside
// today's walk; then the forwarded socket calls sent with no traceparent written first, to see what the transport does;
// then what each walk costs. Nothing here changes Kronikol: the walk is a copy of the design, and the released package
// is only here for TestServer and the factory client's handlers.
// usage: dotnet run --file follow.cs
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using JustEat.HttpClientInterception;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Moq;
using RichardSzalay.MockHttp;

Console.WriteLine($"runtime {Environment.Version}");
Console.WriteLine($"HttpMessageInvoker's fields: {string.Join(", ", typeof(HttpMessageInvoker).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.FieldType.Name + " " + f.Name))}");
Console.WriteLine();

var inMemory = WebApplication.CreateSlimBuilder();
inMemory.Logging.ClearProviders();
inMemory.WebHost.UseTestServer();
var inProcess = inMemory.Build();
inProcess.MapGet("/milk", () => "milk");
await inProcess.StartAsync();
var testServer = inProcess.GetTestServer();
HttpClient FactoryClient() => new(new RedirectHandler { InnerHandler = new CookieContainerHandler { InnerHandler = testServer.CreateHandler() } });

// The issue's first case, built as its library builds it: a handler in an IHttpClientFactory pipeline whose tracker is
// handed the rest of the pipeline through Onward. The walk starts where the tracker's would, at Onward.
var services = new ServiceCollection();
services.AddHttpClient("orders").AddHttpMessageHandler(() => new PerReceiver());
var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
var ordersClient = factory.CreateClient("orders");
var perReceiver = Follow.Chain(Follow.HandlerOf(ordersClient)).OfType<PerReceiver>().Single();

var mockHttp = new MockHttpMessageHandler();
mockHttp.When("*").Respond("text/plain", "milk");
var mockHttpWithFallback = new MockHttpMessageHandler();
mockHttpWithFallback.When("/stubbed").Respond("text/plain", "milk");
mockHttpWithFallback.Fallback.Respond(new HttpClient());
var justEat = new HttpClientInterceptorOptions().CreateHttpMessageHandler();
var router = new Router(new CannedMilk(), new CannedCream());

var shapes = new (string Name, HttpMessageHandler End)[]
{
    ("SocketsHttpHandler", new SocketsHttpHandler()),
    ("HttpClientHandler subclass", new CertificateHandler()),
    ("Onward over sockets", new Onward(new SocketsHttpHandler())),
    ("SendingThrough a socket client", new SendingThrough(new HttpClient(new SocketsHttpHandler()))),
    ("Onward over the factory pipeline", new Onward(perReceiver.InnerHandler!)),
    ("SendingThrough a factory-made client", new SendingThrough(factory.CreateClient("plain"))),
    ("a stub", new CannedMilk()),
    ("Onward over a stub", new Onward(new CannedMilk())),
    ("a stub with a responder", new Responder(_ => new HttpResponseMessage(HttpStatusCode.OK))),
    ("TestServer", testServer.CreateHandler()),
    ("Onward over TestServer", new Onward(testServer.CreateHandler())),
    ("SendingThrough a WAF-style client", new SendingThrough(FactoryClient())),
    ("Moq Mock<HttpMessageHandler>", new Mock<HttpMessageHandler>().Object),
    ("MockHttp", mockHttp),
    ("MockHttp, fallback to a client", mockHttpWithFallback),
    ("JustEat interceptor", justEat),
    ("a router of two stubs", router),
    ("a router of a stub and sockets", new Router(new CannedMilk(), new SocketsHttpHandler())),
    ("a stub holding an unused transport", new Hoarder()),
    ("a handler that holds itself", Looping.Make()),
    ("a lambda forwarder", new Lambda((r, ct) => new HttpClient(new SocketsHttpHandler()).SendAsync(r, ct))),
    ("a forwarder through IHttpClientFactory", new ByFactory(factory)),
};

Console.WriteLine($"{"shape",-40} {"today: mark",-28} {"today: tp",-10} | {"walk: ends",-44} {"mark",-14} {"tp"}");
foreach (var (name, end) in shapes)
{
    var todayEnd = Follow.TodaysEnd(end);
    var todayMark = Follow.TodaysMark(todayEnd);
    var todayTp = todayEnd is SocketsHttpHandler or HttpClientHandler ? "framework" : "Kronikol";
    var ends = Follow.Ends(end);
    var mark = Follow.Mark(ends);
    var tp = Follow.LeaveToFramework(ends) ? "framework" : "Kronikol";
    Console.WriteLine($"{name,-40} {todayMark ?? "-",-28} {todayTp,-10} | {string.Join(", ", ends.Select(e => e.Kind + (e.Kind == EndKind.Answerer ? " " + e.Type.Name : ""))),-44} {mark ?? "-",-14} {tp}");
}

// What a socket transport does with a forwarded call when nothing wrote a traceparent before it: the request it sent is
// the one the response names, and it carries the span the transport started.
Console.WriteLine();
var started = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name is "System.Net.Http" or "probe",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStarted = a => { lock (started) started.Add(a); },
};
ActivitySource.AddActivityListener(listener);
var probe = new ActivitySource("probe");
var kestrel = WebApplication.CreateSlimBuilder();
kestrel.Logging.ClearProviders();
kestrel.WebHost.UseUrls("http://127.0.0.1:0");
var onSocket = kestrel.Build();
onSocket.MapGet("/milk", (HttpRequest r) => r.Headers.TraceParent.ToString());
await onSocket.StartAsync();
var socketUrl = onSocket.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/milk";
foreach (var (name, end) in new (string, HttpMessageHandler)[]
         {
             ("SocketsHttpHandler", new SocketsHttpHandler()),
             ("Onward over sockets", new Onward(new SocketsHttpHandler())),
             ("SendingThrough a socket client", new SendingThrough(new HttpClient(new SocketsHttpHandler()))),
         })
{
    lock (started) started.Clear();
    using var caller = probe.StartActivity("caller")!;
    using var invoker = new HttpMessageInvoker(end);
    var request = new HttpRequestMessage(HttpMethod.Get, socketUrl);
    using var response = await invoker.SendAsync(request, CancellationToken.None);
    var got = await response.Content.ReadAsStringAsync();
    string transport;
    lock (started) transport = started.Single(a => a.OperationName == "System.Net.Http.HttpRequestOut").SpanId.ToString();
    string? Sent(HttpRequestMessage? m) => m is not null && m.Headers.TryGetValues("traceparent", out var v) ? v.First().Substring(36, 16) : null;
    Console.WriteLine($"{name,-32} server's parent is the transport's span: {got.Substring(36, 16) == transport,-5}  " +
                      $"response names the request sent: {(ReferenceEquals(response.RequestMessage, request) ? "the same request" : response.RequestMessage is null ? "none" : "a copy"),-16}  " +
                      $"its traceparent is the transport's: {Sent(response.RequestMessage) == transport,-5}  the original's is: {Sent(request) == transport}");
}

// What each walk costs a call, per shape (the walk runs once per call).
Console.WriteLine();
foreach (var (name, end) in shapes.Where(s => s.Name is "SocketsHttpHandler" or "Onward over sockets" or "Onward over the factory pipeline" or "SendingThrough a WAF-style client" or "a stub"))
{
    const int n = 200_000;
    for (var i = 0; i < 1000; i++) { _ = Follow.Ends(end); _ = Follow.TodaysEnd(end); }
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < n; i++) _ = Follow.TodaysEnd(end);
    var today = sw.Elapsed.TotalNanoseconds / n;
    sw.Restart();
    for (var i = 0; i < n; i++) _ = Follow.Mark(Follow.Ends(end));
    var walk = sw.Elapsed.TotalNanoseconds / n;
    sw.Restart();
    for (var i = 0; i < n; i++) _ = Follow.Resolve(end);
    var resolve = sw.Elapsed.TotalNanoseconds / n;
    Console.WriteLine($"{name,-40} today {today,7:F0} ns   walk {walk,7:F0} ns   walk with today's walk first {resolve,7:F0} ns");
}

await onSocket.StopAsync();
await inProcess.StopAsync();

enum EndKind { Socket, InProcessServer, PlatformTransport, Answerer, Unfollowed }

record End(EndKind Kind, Type Type);

static class Follow
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handler")]
    private static extern ref HttpMessageHandler HandlerField(HttpMessageInvoker invoker);

    public static HttpMessageHandler? HandlerOf(HttpMessageInvoker invoker)
    {
        try { return HandlerField(invoker); }
        catch (MissingFieldException) { return null; }
    }

    private static readonly HashSet<string> Platform = new(StringComparer.Ordinal)
    {
        "System.Net.Http.WinHttpHandler", "System.Net.Http.BrowserHttpHandler", "System.Net.Http.NSUrlSessionHandler",
        "System.Net.Http.CFNetworkHandler", "Foundation.NSUrlSessionHandler", "Xamarin.Android.Net.AndroidMessageHandler",
    };

    private static readonly ConditionalWeakTable<Type, FieldInfo[]> HeldFields = new();

    /// <summary>The fields of a handler's type, and its bases below HttpMessageHandler, that can hold the rest of a chain.</summary>
    private static FieldInfo[] FieldsOf(Type type) => HeldFields.GetValue(type, t =>
    {
        var fields = new List<FieldInfo>();
        for (var level = t; level is not null && level != typeof(HttpMessageHandler); level = level.BaseType)
            foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType) || typeof(HttpMessageInvoker).IsAssignableFrom(field.FieldType))
                    fields.Add(field);
        return [.. fields];
    });

    /// <summary>Every handler the chain passes through, past delegating handlers and the handlers a forwarder holds.</summary>
    public static IEnumerable<HttpMessageHandler> Chain(HttpMessageHandler? start)
    {
        for (var handler = start; handler is not null; handler = (handler as DelegatingHandler)?.InnerHandler)
            yield return handler;
    }

    /// <summary>Where a call this chain is handed goes: one end per path through it.</summary>
    public static List<End> Ends(HttpMessageHandler? start)
    {
        var ends = new List<End>();
        var seen = new HashSet<HttpMessageHandler>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<HttpMessageHandler?>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var handler = pending.Pop();
            while (handler is DelegatingHandler delegating && seen.Add(delegating))
                handler = delegating.InnerHandler;
            if (handler is null)
            {
                ends.Add(new End(EndKind.Unfollowed, typeof(HttpMessageHandler)));
                continue;
            }
            if (handler is DelegatingHandler || !seen.Add(handler))
                continue;
            if (seen.Count > 64)
            {
                ends.Add(new End(EndKind.Unfollowed, handler.GetType()));
                continue;
            }
            if (handler is SocketsHttpHandler or HttpClientHandler)
                ends.Add(new End(EndKind.Socket, handler.GetType()));
            else if (handler.GetType().FullName == "Microsoft.AspNetCore.TestHost.ClientHandler")
                ends.Add(new End(EndKind.InProcessServer, handler.GetType()));
            else if (Platform.Contains(handler.GetType().FullName ?? ""))
                ends.Add(new End(EndKind.PlatformTransport, handler.GetType()));
            else
            {
                var held = new List<HttpMessageHandler?>();
                var unreadable = false;
                // A handler it holds that the walk has passed already (itself, or one before it) is no way onward: a
                // call handed back to it would never end, so it answers.
                foreach (var field in FieldsOf(handler.GetType()))
                    switch (field.GetValue(handler))
                    {
                        case HttpMessageHandler next when !seen.Contains(next): held.Add(next); break;
                        case HttpMessageInvoker invoker:
                            if (HandlerOf(invoker) is not { } inner) unreadable = true;
                            else if (!seen.Contains(inner)) held.Add(inner);
                            break;
                    }
                if (unreadable)
                    ends.Add(new End(EndKind.Unfollowed, handler.GetType()));
                else if (held.Count == 0)
                    ends.Add(new End(EndKind.Answerer, handler.GetType()));
                for (var i = held.Count - 1; i >= 0; i--)
                    pending.Push(held[i]);
            }
        }
        return ends;
    }

    /// <summary>The double that answered: only when every end answers itself, the first of them.</summary>
    public static string? Mark(List<End> ends) =>
        ends.Count > 0 && ends.All(e => e.Kind == EndKind.Answerer) ? ends[0].Type.Name : null;

    /// <summary>Whether the transport writes the traceparent itself: every end is a socket transport.</summary>
    public static bool LeaveToFramework(List<End> ends) => ends.Count > 0 && ends.All(e => e.Kind == EndKind.Socket);

    /// <summary>
    /// Both answers, with today's walk first: a chain of delegating handlers that ends in a transport is answered as today,
    /// with nothing allocated, and only a chain that ends in anything else is walked further.
    /// </summary>
    public static (string? Mark, bool LeaveToFramework) Resolve(HttpMessageHandler? start)
    {
        var end = TodaysEnd(start);
        if (end is SocketsHttpHandler or HttpClientHandler)
            return (null, true);
        if (end is not null && (end.GetType().FullName == "Microsoft.AspNetCore.TestHost.ClientHandler" || Platform.Contains(end.GetType().FullName ?? "")))
            return (null, false);
        var ends = Ends(start);
        return (Mark(ends), LeaveToFramework(ends));
    }

    // Today's walk (TestTrackingMessageHandler.EndOfChain and AnsweringTestDouble, 5.16.0 to 5.27.2).
    public static HttpMessageHandler? TodaysEnd(HttpMessageHandler? handler)
    {
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;
        return handler;
    }

    public static string? TodaysMark(HttpMessageHandler? end) => end switch
    {
        null or SocketsHttpHandler or HttpClientHandler => null,
        var h when h.GetType().FullName == "Microsoft.AspNetCore.TestHost.ClientHandler" || Platform.Contains(h.GetType().FullName ?? "") => null,
        var h => h.GetType().Name,
    };
}

sealed class Onward(HttpMessageHandler next) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _next = new(next, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _next.SendAsync(request, cancellationToken);
}

sealed class SendingThrough(HttpClient client) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, Content = request.Content };
        foreach (var header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return await client.SendAsync(copy, cancellationToken);
    }
}

/// <summary>The issue's library handler, in an IHttpClientFactory pipeline (the tracker itself is left out here).</summary>
sealed class PerReceiver : DelegatingHandler;

sealed class CannedMilk : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("milk") });
}

sealed class CannedCream : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("cream") });
}

sealed class CertificateHandler : HttpClientHandler;

sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

sealed class Router(HttpMessageHandler first, HttpMessageHandler second) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _first = new(first), _second = new(second);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        (request.RequestUri!.AbsolutePath.Contains("cream") ? _second : _first).SendAsync(request, cancellationToken);
}

sealed class Hoarder : HttpMessageHandler
{
    private readonly HttpClientHandler _unused = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_unused.GetType().Name) });
}

sealed class Looping : HttpMessageHandler
{
    private HttpMessageHandler? _self;

    public static Looping Make()
    {
        var looping = new Looping();
        looping._self = looping;
        return looping;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(_self is null ? HttpStatusCode.Gone : HttpStatusCode.OK));
}

sealed class Lambda(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

sealed class ByFactory(IHttpClientFactory factory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        factory.CreateClient("plain").SendAsync(request, cancellationToken);
}
