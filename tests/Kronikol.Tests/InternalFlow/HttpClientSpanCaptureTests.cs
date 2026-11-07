using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kronikol.InternalFlow;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #138: what the span listener keeps of a real <see cref="HttpClient"/> call, and what the call sends
/// (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c> R1). HttpClient makes its span whenever a span is current, heard or not:
/// from <c>System.Net.Http</c>'s source when something listens to it, else with <c>new Activity(...)</c>, which belongs to
/// no source. The listener left <c>System.Net.Http</c> out, which kept out no span and only took the span's source and
/// its kind, so every HttpClient step was drawn in an "Unknown" lane. It hears it now, under a parent only, so a call
/// outside a trace still makes no span and sends no <c>traceparent</c>.
/// </summary>
[Collection(HttpClientSpansCollection.Name)]
public class HttpClientSpanCaptureTests : IDisposable
{
    private readonly string _sourceName = $"Kronikol.Tests.HttpClientSpans.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _parents;
    private readonly Loopback _server = new();

    public HttpClientSpanCaptureTests()
    {
        _source = new ActivitySource(_sourceName);
        // The test's own spans, which the calls are made under; System.Net.Http is left to Kronikol's listener.
        _parents = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(_parents);
    }

    public void Dispose()
    {
        _server.Dispose();
        _parents.Dispose();
        _source.Dispose();
    }

    [Fact]
    public async Task An_HttpClient_call_under_a_span_is_captured_with_its_source()
    {
        using var listener = new InternalFlowActivityListener();
        Activity.Current = null;
        string parentSpan;
        using (var parent = _source.StartActivity("place an order")!)
        {
            parentSpan = parent.SpanId.ToString();
            await Get(_server.Url("/orders"));
        }

        var span = Assert.Single(SpansUnder(parentSpan));
        Assert.Equal("System.Net.Http", span.Source.Name);
        Assert.Equal(ActivityKind.Client, span.Kind);
        // The span captured is the one the call sent.
        var sent = ActivityContext.Parse(Assert.Single(_server.TraceParents), null);
        Assert.Equal(span.SpanId, sent.SpanId);
    }

    [Fact]
    public async Task An_HttpClient_call_outside_any_span_makes_no_span_and_sends_no_traceparent()
    {
        // Hearing System.Net.Http with no condition would make every call outside a trace a root span and put a
        // traceparent on it, which changes what goes on the wire (the plan's section 2.2, "include").
        using var listener = new InternalFlowActivityListener();
        var before = DateTime.UtcNow;
        Activity.Current = null;

        await Get(_server.Url("/health"));

        Assert.Empty(_server.TraceParents);
        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
            s => s.StartTimeUtc >= before && s.OperationName == "System.Net.Http.HttpRequestOut"
                 && Equals(s.GetTagItem("server.port"), _server.Port));
    }

    [Fact]
    public async Task The_runtime_connection_spans_are_not_heard()
    {
        // From .NET 9 the runtime builds spans for each connection (socket connect, DNS lookup, connection setup, the
        // wait for a connection) when something listens to its Experimental.System.Net sources: noise, which a listener
        // that heard every source kept for the whole run.
        using var listener = new InternalFlowActivityListener();
        var before = DateTime.UtcNow;
        Activity.Current = null;

        using (_source.StartActivity("place an order"))
            await Get(_server.Url("/orders"));

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
            s => s.StartTimeUtc >= before && s.Source.Name.StartsWith("Experimental.System.Net.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Application_Insights_records_the_same_http_dependencies_with_the_listener(bool listenerFirst)
    {
        // Why System.Net.Http was left unheard from 2.21.1 on: hearing it was thought to stop the events Application
        // Insights 2.x builds its HTTP dependencies from. .NET writes them whichever way it makes the span, and the break
        // 2.21.1 fixed was the handler's own traceparent, which Application Insights skips on purpose (the plan's
        // section 2.3). This keeps that measured, with the listener started before the module and after it.
        var channel = new CapturingChannel();
        using var configuration = new TelemetryConfiguration
        {
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=http://127.0.0.1:1/",
            TelemetryChannel = channel
        };
        configuration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
        using var module = new DependencyTrackingTelemetryModule();
        var listener = listenerFirst ? new InternalFlowActivityListener() : null;
        module.Initialize(configuration);
        listener ??= new InternalFlowActivityListener();

        using (listener)
        {
            Activity.Current = null;
            using var parent = _source.StartActivity("place an order")!;
            for (var i = 0; i < 3; i++)
                await Get(_server.Url($"/items/{i}"));
            parent.Stop();

            var dependencies = await Dependencies(channel, count: 3);
            var spans = SpansUnder(parent.SpanId.ToString());
            Assert.Equal(3, dependencies.Length);
            Assert.Equal(3, spans.Length);
            Assert.All(dependencies, d =>
            {
                Assert.Contains(spans, s => s.SpanId.ToHexString() == d.Id);
                Assert.Equal(parent.TraceId.ToHexString(), d.Context.Operation.Id);
                Assert.Equal(parent.SpanId.ToHexString(), d.Context.Operation.ParentId);
            });
        }
    }

    private static Activity[] SpansUnder(string parentSpanId) =>
        InternalFlowSpanStore.GetSpans().Where(s => s.ParentSpanId.ToString() == parentSpanId).ToArray();

    private static async Task Get(Uri uri)
    {
        // A handler of its own, so each call opens a connection.
        using var client = new HttpClient(new SocketsHttpHandler());
        using var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The HTTP dependencies the module sent, once there are <paramref name="count"/>, or after five seconds.</summary>
    private static async Task<DependencyTelemetry[]> Dependencies(CapturingChannel channel, int count)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        DependencyTelemetry[] found;
        while ((found = channel.Items.OfType<DependencyTelemetry>().Where(d => d.Type == "Http").ToArray()).Length < count
               && DateTime.UtcNow < until)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        return found;
    }

    private sealed class CapturingChannel : ITelemetryChannel
    {
        public ConcurrentQueue<ITelemetry> Items { get; } = new();
        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }
        public void Send(ITelemetry item) => Items.Enqueue(item);
        public void Flush() { }
        public void Dispose() { }
    }

    /// <summary>
    /// An HTTP/1.1 server on a loopback port that keeps the <c>traceparent</c> each request carried and answers each
    /// with 200. A socket of its own rather than HttpListener: no URL reservation, on any platform.
    /// </summary>
    private sealed class Loopback : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public Loopback()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        public int Port { get; }

        public ConcurrentQueue<string> TraceParents { get; } = new();

        public Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");

        private async Task Serve()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (Exception) when (_stop.IsCancellationRequested) { return; }
                _ = Task.Run(() => Answer(client));
            }
        }

        private async Task Answer(TcpClient client)
        {
            using var connection = client;
            try
            {
                var stream = connection.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (true)
                {
                    var any = false;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                    {
                        any = true;
                        if (line.StartsWith("traceparent:", StringComparison.OrdinalIgnoreCase))
                            TraceParents.Enqueue(line["traceparent:".Length..].Trim());
                    }
                    if (!any)
                        return;
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray(), _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // The client closed the connection.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
