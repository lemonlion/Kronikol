#:sdk Microsoft.NET.Sdk.Web
#:package Kronikol@5.27.2
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
// P1 (#219): the issue's two forwarders, and the same shapes over a stub and over TestServer, each tracked by the released
// TestTrackingMessageHandler. Per shape it prints what the handler wrote as testDouble, the traceparent the server got,
// the span the transport started for the call, and the span the call's response names; then it writes a report of the
// calls for `query parties` (run.sh).
// usage: dotnet run --file forwarders.cs -- <reports dir>
using System.Diagnostics;
using System.Net;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;

var reports = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "kronikol-219-p1");

// Every span the socket transport starts (System.Net.Http.HttpRequestOut), and the caller's own, are created and kept.
var started = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name is "System.Net.Http" or "probe",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStarted = a => { lock (started) started.Add(a); },
};
ActivitySource.AddActivityListener(listener);
var probe = new ActivitySource("probe");

// A service on a socket: Kestrel on a free loopback port, answering with the traceparent it was sent.
var kestrel = WebApplication.CreateSlimBuilder();
kestrel.Logging.ClearProviders();
kestrel.WebHost.UseUrls("http://127.0.0.1:0");
var onSocket = kestrel.Build();
onSocket.MapGet("/milk", (HttpRequest r) => r.Headers.TraceParent.ToString());
await onSocket.StartAsync();
var socketUrl = onSocket.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

// A service in process: the same endpoint on TestServer, as a WebApplicationFactory hosts it.
var inMemory = WebApplication.CreateSlimBuilder();
inMemory.Logging.ClearProviders();
inMemory.WebHost.UseTestServer();
var inProcess = inMemory.Build();
inProcess.MapGet("/milk", (HttpRequest r) => r.Headers.TraceParent.ToString());
await inProcess.StartAsync();
var testServer = inProcess.GetTestServer();
// What a WebApplicationFactory's CreateClient() builds: its redirect and cookie handlers over TestServer's handler.
HttpClient FactoryClient() => new(new RedirectHandler { InnerHandler = new CookieContainerHandler { InnerHandler = testServer.CreateHandler() } })
    { BaseAddress = new Uri("http://localhost") };

var shapes = new (string Service, string Url, Func<HttpMessageHandler> End)[]
{
    ("Sockets", socketUrl, () => new SocketsHttpHandler()),
    ("Onward over sockets", socketUrl, () => new Onward(new SocketsHttpHandler())),
    ("SendingThrough a socket client", socketUrl, () => new SendingThrough(new HttpClient(new SocketsHttpHandler()))),
    ("Stub", socketUrl, () => new CannedMilk()),
    ("Onward over a stub", socketUrl, () => new Onward(new CannedMilk())),
    ("TestServer", "http://localhost", () => testServer.CreateHandler()),
    ("Onward over TestServer", "http://localhost", () => new Onward(testServer.CreateHandler())),
    ("SendingThrough a factory client", "http://localhost", () => new SendingThrough(FactoryClient())),
};

var scenarios = new List<Scenario>();
var start = DateTime.UtcNow;
Console.WriteLine($"Kronikol {typeof(TestTrackingMessageHandler).Assembly.GetName().Version}, runtime {Environment.Version}");
Console.WriteLine();
Console.WriteLine($"{"shape",-32} {"testDouble",-16} {"server's parent",-16} {"transport span",-16} {"response names",-16}");
foreach (var (service, url, end) in shapes)
{
    var testId = Guid.NewGuid().ToString();
    var tracker = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
    {
        CallerName = "Breakfast Provider",
        FixedNameForReceivingService = service,
        CurrentTestInfoFetcher = () => ("Order milk: " + service, testId),
    }) { InnerHandler = end() };
    using var invoker = new HttpMessageInvoker(tracker);
    lock (started) started.Clear();
    string seen;
    // The call is made under a span, as a host serving a request makes it.
    using (var caller = probe.StartActivity("caller")!)
    {
        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/milk"), CancellationToken.None);
        seen = await response.Content.ReadAsStringAsync();
        var parent = seen.Length == 55 ? seen.Substring(36, 16) : "(none)";
        string transport;
        lock (started) transport = started.FirstOrDefault(a => a.OperationName == "System.Net.Http.HttpRequestOut")?.SpanId.ToString() ?? "(none)";
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var mark = string.Join("/", logs.Select(l => l.TestDouble ?? "-").Distinct());
        var named = logs.Single(l => l.Type == RequestResponseType.Response).ActivitySpanId;
        string Who(string? span) => span == caller.SpanId.ToString() ? "caller's span" : span == transport ? "transport's" : span ?? "(none)";
        Console.WriteLine($"{service,-32} {mark,-16} {Who(parent),-16} {(transport == "(none)" ? "(none)" : "started"),-16} {Who(named),-16}");
    }
    scenarios.Add(new Scenario { Id = testId, DisplayName = "Order milk: " + service, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) });
}

ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Forwarders", Scenarios = [.. scenarios] }],
    start, DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = reports,
        SuiteName = "Forwarders",
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
    });
Console.WriteLine();
Console.WriteLine("report written to " + reports);
await onSocket.StopAsync();
await inProcess.StopAsync();

/// <summary>The issue's first forwarder: the rest of the pipeline, handed on and never disposed.</summary>
sealed class Onward(HttpMessageHandler next) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _next = new(next, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _next.SendAsync(request, cancellationToken);
}

/// <summary>The issue's second forwarder: a copy of each request, sent through a client the test already holds.</summary>
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

/// <summary>A stub: it answers every call itself.</summary>
sealed class CannedMilk : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Headers.TryGetValues("traceparent", out var v) ? v.First() : "") });
}
