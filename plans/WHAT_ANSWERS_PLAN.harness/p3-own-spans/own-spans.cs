#:package Kronikol@5.27.2
#:property PublishAot=false
// P3 (#219): CallOwnSpanThroughTheHandlerTests.Calls_a_host_makes_at_once_each_open_the_spans_of_their_own_call (4.26.5)
// run on the released handler with the issue's two forwarders between it and the socket transport. A host makes three
// calls at once under its request span; each call's internal-flow popup should hold its transport's span and the service's
// work, and none of the others'.
// usage: dotnet run --file own-spans.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

var source = new ActivitySource("probe.own-spans");
var spans = new ConcurrentQueue<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == source.Name || s.Name == "System.Net.Http",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = spans.Enqueue,
};
ActivitySource.AddActivityListener(listener);
using var downstream = new Downstream(source);

Console.WriteLine($"Kronikol {typeof(TestTrackingMessageHandler).Assembly.GetName().Version}, runtime {Environment.Version}");
foreach (var (shape, end) in new (string, Func<HttpMessageHandler>)[]
         {
             ("SocketsHttpHandler", () => new SocketsHttpHandler()),
             ("Onward over sockets", () => new Onward(new SocketsHttpHandler())),
             ("SendingThrough a socket client", () => new SendingThrough(new HttpClient(new SocketsHttpHandler()))),
         })
{
    var testId = Guid.NewGuid().ToString("N");
    Activity.Current = null;
    using (source.StartActivity("host request")!)
        await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Call(testId, downstream.Url($"/service-{i}/health"), end())));

    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
    var segments = InternalFlowSegmentBuilder.BuildSegments(logs, spans.ToArray());
    Console.WriteLine();
    Console.WriteLine(shape);
    foreach (var request in logs.Where(l => l.Type == RequestResponseType.Request).OrderBy(l => l.Uri.AbsolutePath))
    {
        var own = segments.TryGetValue($"iflow-{request.RequestResponseId}", out var segment) ? segment.Spans : [];
        var names = own.Select(s => s.Source.Name == "System.Net.Http" ? "transport" : s.DisplayName).Order().ToArray();
        var others = own.Count(s => s.DisplayName.StartsWith("server ") && s.DisplayName != $"server {request.Uri.AbsolutePath}");
        Console.WriteLine($"  {request.Uri.AbsolutePath,-22} its popup: {own.Length} spans, {others} of the other calls' work: {string.Join(", ", names)}");
    }
}

static async Task Call(string testId, Uri uri, HttpMessageHandler end)
{
    var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
    {
        CallerName = "Host",
        FixedNameForReceivingService = "Downstream",
        CurrentTestInfoFetcher = () => ("Own span", testId),
    }) { InnerHandler = end };
    using var invoker = new HttpMessageInvoker(handler);
    using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), CancellationToken.None);
    if (response.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException(response.StatusCode.ToString());
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

/// <summary>The services the host calls, as the fact has them: each request's work under a server span continuing its traceparent.</summary>
sealed class Downstream : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly ActivitySource _source;

    public Downstream(ActivitySource source)
    {
        _source = source;
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        _port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _ = Task.Run(Serve);
    }

    public Uri Url(string path) => new($"http://localhost:{_port}{path}");

    private async Task Serve()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening) { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var traceParent = context.Request.Headers["traceparent"];
        var parent = traceParent is not null && ActivityContext.TryParse(traceParent, null, out var sent) ? sent : default;
        var previous = Activity.Current;
        Activity.Current = null;
        using (_source.StartActivity($"server {context.Request.Url!.AbsolutePath}", ActivityKind.Server, parent))
            Thread.Sleep(5);
        Activity.Current = previous;
        context.Response.StatusCode = 200;
        context.Response.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _stop.Dispose();
    }
}
