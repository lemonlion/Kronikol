using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// A call a host makes over a socket, through the real <see cref="TestTrackingMessageHandler"/> and
/// <see cref="SocketsHttpHandler"/>. The handler sees the call while the host's request span is current, and logs the
/// request under it; the transport starts a span of its own for the call later, and sends that one in the call's
/// <c>traceparent</c>. The call's response names it, so the calls a host makes under one request are told apart: in the
/// owner's report every call BreakfastProvider made to the services it checks at once opened its whole request's flow.
/// </summary>
[Collection("DiagramsFetcher")]
public class CallOwnSpanThroughTheHandlerTests : IDisposable
{
    private readonly string _sourceName = $"Kronikol.Tests.CallOwnSpan.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _spans = new();

    public CallOwnSpanThroughTheHandlerTests()
    {
        _source = new ActivitySource(_sourceName);
        // The host's spans and the transport's: System.Net.Http's source starts the span of each call it sends.
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName || s.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public async Task A_call_made_under_a_span_names_on_its_response_the_span_its_transport_started_for_it()
    {
        var testId = "own-span-" + Guid.NewGuid().ToString("N");
        using var downstream = new Downstream(_source);

        Activity.Current = null;
        string hostSpan;
        using (var host = _source.StartActivity("host request")!)
        {
            hostSpan = host.SpanId.ToString();
            await Call(testId, downstream.Url("/health"));
        }

        var (request, response) = Logs(testId);
        var sent = ActivityContext.Parse(Assert.Single(downstream.TraceParents), null);
        Assert.Equal(hostSpan, request.ActivitySpanId);
        Assert.Equal(request.ActivityTraceId, sent.TraceId.ToString());
        Assert.NotEqual(hostSpan, sent.SpanId.ToString());
        Assert.Equal(sent.SpanId.ToString(), response.ActivitySpanId);
        Assert.Equal(request.ActivityTraceId, response.ActivityTraceId);
    }

    [Fact]
    public async Task A_call_whose_request_carries_a_traceparent_already_keeps_the_span_it_was_made_under()
    {
        // The caller's own traceparent (one it set, or HeadersToForward copied) is left as it is: the span it names is
        // the caller's, not one the transport started.
        var testId = "own-span-set-" + Guid.NewGuid().ToString("N");
        using var downstream = new Downstream(_source);

        Activity.Current = null;
        string hostSpan;
        using (var host = _source.StartActivity("host request")!)
        {
            hostSpan = host.SpanId.ToString();
            var set = $"00-{host.TraceId}-{ActivitySpanId.CreateRandom()}-01";
            await Call(testId, downstream.Url("/health"), request => request.Headers.TryAddWithoutValidation("traceparent", set));
        }

        var (request, response) = Logs(testId);
        Assert.Equal(hostSpan, request.ActivitySpanId);
        Assert.Equal(hostSpan, response.ActivitySpanId);
    }

    [Fact]
    public async Task A_call_through_a_transport_that_starts_no_span_keeps_the_span_it_was_made_under()
    {
        // An in-memory transport (TestServer's) starts no span and sends what the handler put on the request.
        var testId = "own-span-memory-" + Guid.NewGuid().ToString("N");

        Activity.Current = null;
        string hostSpan;
        using (var host = _source.StartActivity("host request")!)
        {
            hostSpan = host.SpanId.ToString();
            await Call(testId, new Uri("http://orders/health"), inner: new Answer());
        }

        var (request, response) = Logs(testId);
        Assert.Equal(hostSpan, request.ActivitySpanId);
        Assert.Equal(hostSpan, response.ActivitySpanId);
    }

    [Fact]
    public async Task Calls_a_host_makes_at_once_each_open_the_spans_of_their_own_call()
    {
        // BreakfastProvider's health check through the real pipeline: the host checks three services at once under its
        // request span. Each call's popup holds its transport's span and the service's work, and none of the others'.
        var testId = "own-span-many-" + Guid.NewGuid().ToString("N");
        using var downstream = new Downstream(_source);

        Activity.Current = null;
        using (_source.StartActivity("host request")!)
            await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Call(testId, downstream.Url($"/service-{i}/health"))));

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var segments = InternalFlowSegmentBuilder.BuildSegments(logs, _spans.ToArray());

        var requests = logs.Where(l => l.Type == RequestResponseType.Request).ToArray();
        Assert.Equal(3, requests.Length);
        foreach (var request in requests)
        {
            var own = segments[$"iflow-{request.RequestResponseId}"].Spans;
            var path = request.Uri.AbsolutePath;
            Assert.Equal(2, own.Length);
            Assert.Contains(own, s => s.Source.Name == "System.Net.Http");
            Assert.Contains(own, s => s.DisplayName == $"server {path}");
            Assert.DoesNotContain(own, s => s.DisplayName == "host request");
        }
    }

    private static async Task Call(string testId, Uri uri, Action<HttpRequestMessage>? prepare = null, HttpMessageHandler? inner = null)
    {
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Host",
            FixedNameForReceivingService = "Downstream",
            CurrentTestInfoFetcher = () => ("Own span", testId),
        })
        {
            InnerHandler = inner ?? new SocketsHttpHandler()
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        prepare?.Invoke(request);
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static (RequestResponseLog Request, RequestResponseLog Response) Logs(string testId)
    {
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        return (Assert.Single(logs, l => l.Type == RequestResponseType.Request), Assert.Single(logs, l => l.Type == RequestResponseType.Response));
    }

    /// <summary>A transport that starts no span: it answers every request itself.</summary>
    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    /// <summary>
    /// The services the host calls: a listener on a loopback port that keeps each <c>traceparent</c> it is sent and does
    /// its work under a server span continuing it, named for the path.
    /// </summary>
    private sealed class Downstream : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly int _port = LoopbackPort.Free();
        private readonly CancellationTokenSource _stop = new();
        private readonly ActivitySource _source;

        public ConcurrentQueue<string> TraceParents { get; } = new();

        public Downstream(ActivitySource source)
        {
            _source = source;
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
            if (traceParent is not null)
                TraceParents.Enqueue(traceParent);
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
            // No Stop() before Close: on Linux that binds the port again (HttpListenerLifetimeTests).
            _listener.Close();
            _stop.Dispose();
        }
    }
}
