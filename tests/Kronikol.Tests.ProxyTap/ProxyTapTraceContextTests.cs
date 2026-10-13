using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// An <see cref="ActivityListener"/> is process-wide, so the facts that attach one (and the facts that need none
/// attached) run in a collection of their own, after and apart from every other collection.
/// </summary>
[CollectionDefinition(nameof(ProxyTapTraceContextTests), DisableParallelization = true)]
public sealed class ProxyTapTraceContextCollection;

/// <summary>Which trace a tap forwards, under which parent and with which sampled flag (#170).</summary>
[Collection(nameof(ProxyTapTraceContextTests))]
public class ProxyTapTraceContextTests
{
    private static readonly Regex Traceparent = new("^00-(?<trace>[0-9a-f]{32})-(?<span>[0-9a-f]{16})-(?<flags>[0-9a-f]{2})$");

    private static (int, byte[], string?, string?, Dictionary<string, string>?) Ok(HttpListenerRequest _, byte[] __) =>
        (200, Encoding.UTF8.GetBytes("{}"), "application/json", null, null);

    private static string NewTrace() => ActivityTraceId.CreateRandom().ToString();

    private static string NewSpan() => ActivitySpanId.CreateRandom().ToString();

    private sealed record TapRun(
        List<string?> Forwarded, List<RequestResponseLog> Logs, IReadOnlyList<DiagnosticEntry> Diagnostics, List<string> LogLines);

    /// <summary>
    /// Sends the requests in order through a fresh tap, each with its <c>traceparent</c> (none when null) and headers,
    /// and returns the <c>traceparent</c> the upstream received for each, the tap's records, its diagnostics and its log.
    /// </summary>
    private static async Task<TapRun> RunAsync(
        Action<ProxyTapOptions>? configure, params (string? Traceparent, (string Name, string Value)[] Headers)[] requests)
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var lines = new List<string>();
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "api",
            Sink = sink,
            Log = line => { lock (lines) lines.Add(line); },
        };
        configure?.Invoke(options);
        var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(options);
        try
        {
            await tap.StartAsync();
            // A caller that sends what the fact says and nothing else: no handler adds a trace whatever is listening.
            using var client = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null });
            foreach (var (traceparent, headers) in requests)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/i"));
                foreach (var (name, value) in headers)
                    request.Headers.TryAddWithoutValidation(name, value);
                if (traceparent is not null)
                    request.Headers.TryAddWithoutValidation("traceparent", traceparent);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            // Disposing waits for the handlers still recording, so the counters behind Diagnostics() have settled.
            await tap.DisposeAsync();
        }

        var diagnostics = tap.Diagnostics();

        lock (upstream.Seen)
        lock (lines)
            return new TapRun(
                upstream.Seen.Select(s => s.Headers.TryGetValue("traceparent", out var v) ? v : null).ToList(),
                sink.Logs.ToList(), diagnostics, lines.ToList());
    }

    /// <summary>One request per entry (a null entry sends no <c>traceparent</c>), each naming test <c>t</c> by header.</summary>
    private static async Task<(List<string?> Forwarded, List<RequestResponseLog> Logs)> SendAsync(
        Action<ProxyTapOptions>? configure, params string?[] traceparents)
    {
        var run = await RunAsync(configure,
            traceparents.Select(t => (t, new[] { (TestTrackingHttpHeaders.CurrentTestIdHeader, "t") })).ToArray());
        return (run.Forwarded, run.Logs);
    }

    /// <summary>Listens to the tap's source only, with the given sampler, and keeps the spans it stops.</summary>
    private sealed class TapListener : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _stopped = [];

        public TapListener(SampleActivity<ActivityContext> sample)
        {
            _listener = new ActivityListener
            {
                // The constant, never ProxyTap.ActivitySource.Name: reading that field before the type is
                // initialised throws TypeInitializationException as soon as any ActivitySource is created.
                ShouldListenTo = s => s.Name == Kronikol.Extensions.ProxyTap.ProxyTap.ActivitySourceName,
                Sample = sample,
                ActivityStopped = a => { lock (_stopped) _stopped.Add(a); },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Stopped { get { lock (_stopped) return _stopped.ToList(); } }

        public void Dispose() => _listener.Dispose();
    }

    private static Match Parse(string? forwarded)
    {
        Assert.NotNull(forwarded);
        var match = Traceparent.Match(forwarded);
        Assert.True(match.Success, $"not a traceparent: {forwarded}");
        return match;
    }

    // ---- R0: F2, the client span keeps the caller's trace when a listener drops the server span -----------

    [Fact]
    public async Task An_unsampled_inbound_trace_is_forwarded_unchanged_when_the_listener_drops_the_server_span()
    {
        // Drops a remote parent that is not sampled, samples everything else: a root included.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> o) =>
            o.Parent != default && o.Parent.IsRemote && (o.Parent.TraceFlags & ActivityTraceFlags.Recorded) == 0
                ? ActivitySamplingResult.None
                : ActivitySamplingResult.AllDataAndRecorded);
        var trace = NewTrace();
        var inbound = $"00-{trace}-{NewSpan()}-00";

        var (forwarded, logs) = await SendAsync(null, inbound);

        Assert.Equal(inbound, forwarded.Single());
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.Equal(trace, l.ActivityTraceId));
    }

    [Fact]
    public async Task A_sampler_that_keeps_only_the_client_span_parents_it_on_the_inbound_trace()
    {
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> o) =>
            o.Kind == ActivityKind.Server ? ActivitySamplingResult.None : ActivitySamplingResult.AllDataAndRecorded);
        var trace = NewTrace();
        var parentSpan = NewSpan();

        var (forwarded, logs) = await SendAsync(null, $"00-{trace}-{parentSpan}-01");

        var header = Parse(forwarded.Single());
        Assert.Equal(trace, header.Groups["trace"].Value);
        Assert.Equal("01", header.Groups["flags"].Value);
        var client = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
        Assert.Equal(ActivityKind.Client, client.Kind);
        Assert.Equal(trace, client.TraceId.ToString());
        Assert.Equal(parentSpan, client.ParentSpanId.ToString());
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.Equal((trace, client.SpanId.ToString()), (l.ActivityTraceId, l.ActivitySpanId)));
    }

    [Fact]
    public async Task A_listener_that_only_propagates_leaves_the_callers_header_unchanged()
    {
        // What the OpenTelemetry SDK does with a trace its sampler drops: PropagationData for a root or a remote
        // parent, None for a local child. The tap then takes its branch without a client span.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> o) =>
            o.Parent == default || o.Parent.IsRemote ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.None);
        var sampled = $"00-{NewTrace()}-{NewSpan()}-01";
        var unsampled = $"00-{NewTrace()}-{NewSpan()}-00";

        var (forwarded, _) = await SendAsync(null, sampled, unsampled, null);

        Assert.Equal(sampled, forwarded[0]);
        Assert.Equal(unsampled, forwarded[1]);
        Assert.Equal("01", Parse(forwarded[2]).Groups["flags"].Value);
    }

    // ---- R0: F3, the forwarded flags follow the caller, not an unrecorded span ---------------------------

    [Fact]
    public async Task An_unrecorded_forward_span_keeps_the_callers_sampled_flag()
    {
        // AllData, as Kronikol's internal-flow listener samples: spans exist but are not recorded.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData);
        var sampledTrace = NewTrace();
        var unsampledTrace = NewTrace();

        var (forwarded, logs) = await SendAsync(null,
            $"00-{sampledTrace}-{NewSpan()}-01", $"00-{unsampledTrace}-{NewSpan()}-00", null);

        var first = Parse(forwarded[0]);
        Assert.Equal(sampledTrace, first.Groups["trace"].Value);
        Assert.Equal("01", first.Groups["flags"].Value);
        var second = Parse(forwarded[1]);
        Assert.Equal(unsampledTrace, second.Groups["trace"].Value);
        Assert.Equal("00", second.Groups["flags"].Value);
        var third = Parse(forwarded[2]);
        Assert.Equal("01", third.Groups["flags"].Value);

        // Each header names the tap's own client span, and the records carry its ids.
        for (var i = 0; i < 3; i++)
        {
            var header = Parse(forwarded[i]);
            var client = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
            Assert.Equal(ActivityKind.Client, client.Kind);
            Assert.False(client.Recorded);
            Assert.Contains(logs, l => l.ActivityTraceId == header.Groups["trace"].Value && l.ActivitySpanId == header.Groups["span"].Value);
        }
    }

    [Fact]
    public async Task A_recorded_forward_span_is_forwarded_sampled()
    {
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded);
        var trace = NewTrace();

        var (forwarded, _) = await SendAsync(null, $"00-{trace}-{NewSpan()}-00");

        var header = Parse(forwarded.Single());
        Assert.Equal(trace, header.Groups["trace"].Value);
        Assert.Equal("01", header.Groups["flags"].Value);
    }

    [Fact]
    public async Task Kronikols_internal_flow_listener_does_not_clear_the_sampled_flag()
    {
        // The listener TestTrackingMessageHandler starts in a test process, attached for this fact only.
        using var listener = new InternalFlowActivityListener();
        var trace = NewTrace();

        var (forwarded, _) = await SendAsync(null, $"00-{trace}-{NewSpan()}-01", null);

        var first = Parse(forwarded[0]);
        Assert.Equal(trace, first.Groups["trace"].Value);
        Assert.Equal("01", first.Groups["flags"].Value);
        Assert.Equal("01", Parse(forwarded[1]).Groups["flags"].Value);
    }

    // ---- R1: TraceIdFromIdentity -------------------------------------------------------------------------

    private const string TestId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly (string, string)[] NoHeaders = [];

    private static void FromIdentity(ProxyTapOptions options)
    {
        options.TraceIdFromIdentity = true;
        options.IdentityResolver = (_, _) => ("places an order", TestId);
    }

    private static string TraceOf(string? forwarded) => Parse(forwarded).Groups["trace"].Value;

    [Fact]
    public async Task With_TraceIdFromIdentity_a_resolved_test_id_becomes_the_forwarded_trace()
    {
        var run = await RunAsync(FromIdentity, (null, NoHeaders), (null, NoHeaders));

        var first = Parse(run.Forwarded[0]);
        var second = Parse(run.Forwarded[1]);
        Assert.Equal(TestId, first.Groups["trace"].Value);
        Assert.Equal(TestId, second.Groups["trace"].Value);
        Assert.Equal("01", first.Groups["flags"].Value);
        Assert.NotEqual(first.Groups["span"].Value, second.Groups["span"].Value);
        Assert.All(run.Logs, l => Assert.Equal((TestId, TestId), (l.TestId, l.ActivityTraceId)));
        Assert.Contains(run.Logs, l => l.ActivitySpanId == first.Groups["span"].Value);
        Assert.Contains(run.Logs, l => l.ActivitySpanId == second.Groups["span"].Value);
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public async Task With_a_listener_the_server_span_is_a_root_of_the_test_trace()
    {
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded);

        var run = await RunAsync(FromIdentity, (null, NoHeaders));

        var header = Parse(run.Forwarded.Single());
        var client = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
        var server = listener.Stopped.Single(a => a.Kind == ActivityKind.Server && a.SpanId == client.ParentSpanId);
        Assert.Equal(TestId, server.TraceId.ToString());
        Assert.Equal(default, server.ParentSpanId);
        Assert.True(server.HasRemoteParent);
        Assert.Null(server.Parent);
        Assert.Equal(TestId, client.TraceId.ToString());
        Assert.Same(server, client.Parent);
        Assert.Equal(TestId, header.Groups["trace"].Value);
        Assert.Equal("01", header.Groups["flags"].Value);
        Assert.All(run.Logs, l => Assert.Equal((TestId, client.SpanId.ToString()), (l.ActivityTraceId, l.ActivitySpanId)));
    }

    [Fact]
    public async Task With_an_AllData_listener_the_test_trace_is_forwarded_sampled()
    {
        using var listener = new InternalFlowActivityListener();

        var run = await RunAsync(FromIdentity, (null, NoHeaders));

        var header = Parse(run.Forwarded.Single());
        Assert.Equal(TestId, header.Groups["trace"].Value);
        Assert.Equal("01", header.Groups["flags"].Value);
        Assert.All(run.Logs, l => Assert.Equal((TestId, header.Groups["span"].Value), (l.ActivityTraceId, l.ActivitySpanId)));
    }

    [Fact]
    public async Task The_test_trace_survives_a_sampler_that_drops_the_server_span()
    {
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> o) =>
            o.Kind == ActivityKind.Server ? ActivitySamplingResult.None : ActivitySamplingResult.AllDataAndRecorded);

        var run = await RunAsync(FromIdentity, (null, NoHeaders));

        var header = Parse(run.Forwarded.Single());
        Assert.Equal(TestId, header.Groups["trace"].Value);
        var client = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
        Assert.Equal(TestId, client.TraceId.ToString());
        Assert.Equal(default, client.ParentSpanId);
    }

    [Fact]
    public async Task Under_a_listener_that_only_propagates_the_test_trace_is_forwarded()
    {
        // The SDK's shape for a trace it drops, as under AlwaysOff: the server span exists unrecorded, no client span.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> o) =>
            o.Parent == default || o.Parent.IsRemote ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.None);

        var run = await RunAsync(FromIdentity, (null, NoHeaders));

        var header = Parse(run.Forwarded.Single());
        Assert.Equal(TestId, header.Groups["trace"].Value);
        Assert.Equal("01", header.Groups["flags"].Value);
        Assert.All(run.Logs, l => Assert.Equal((TestId, header.Groups["span"].Value), (l.ActivityTraceId, l.ActivitySpanId)));
    }

    [Fact]
    public async Task An_inbound_traceparent_keeps_its_trace_with_TraceIdFromIdentity_on()
    {
        var inbound = $"00-{NewTrace()}-{NewSpan()}-01";

        var run = await RunAsync(FromIdentity, (inbound, NoHeaders));

        Assert.Equal(inbound, run.Forwarded.Single());
        Assert.Empty(run.Diagnostics);

        // Nor is such a request counted: the option does not apply to a request that brings a trace, whatever its id.
        var guid = await RunAsync(o => { o.TraceIdFromIdentity = true; o.IdentityResolver = (_, _) => ("places an order", Guid.NewGuid().ToString()); },
            (inbound, NoHeaders));
        Assert.Equal(inbound, guid.Forwarded.Single());
        Assert.Empty(guid.Diagnostics);
    }

    [Fact]
    public async Task The_server_span_is_a_root_of_the_test_trace_when_the_tap_started_under_an_activity()
    {
        // A tap started inside a test or a fixture under an activity hands that activity to every request it handles
        // as Activity.Current, since its accept loop runs in the context it was started in. The test's trace is still
        // a root of its own, not a child of whatever was current at start-up.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded);
        await using var upstream = new StubUpstream(Ok);
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = upstream.BaseUri, CallerName = "web", ServiceName = "api", Sink = new ListSink(),
        };
        FromIdentity(options);
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(options);
        var ambient = new Activity("ambient").Start();
        try
        {
            await tap.StartAsync();
        }
        finally
        {
            ambient.Stop();
            Activity.Current = null;
        }

        using (var client = new HttpClient())
        using (var response = await client.GetAsync(new Uri(tap.ListenUri, "/i")))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string forwarded;
        lock (upstream.Seen)
            forwarded = upstream.Seen.Single().Headers["traceparent"];
        var header = Parse(forwarded);
        Assert.Equal(TestId, header.Groups["trace"].Value);
        var forward = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
        var server = listener.Stopped.Single(a => a.Kind == ActivityKind.Server && a.SpanId == forward.ParentSpanId);
        Assert.Equal(TestId, server.TraceId.ToString());
        Assert.Equal(default, server.ParentSpanId);
        Assert.Null(server.Parent);
    }

    [Fact]
    public async Task Starting_a_tap_leaves_the_callers_current_activity_alone()
    {
        // StartAsync is not an async method, so what it does to Activity.Current would stay with its caller: the tap
        // starts its accept loop with no current activity and gives the caller's back.
        await using var upstream = new StubUpstream(Ok);
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = upstream.BaseUri, CallerName = "web", ServiceName = "api", Sink = new ListSink(),
        });
        var fixture = new Activity("fixture").Start();
        try
        {
            await tap.StartAsync();
            Assert.Same(fixture, Activity.Current);
        }
        finally
        {
            fixture.Stop();
            Activity.Current = null;
        }
    }

    [Fact]
    public async Task The_server_span_is_a_root_of_the_test_trace_when_the_resolver_leaves_an_activity_current()
    {
        // The resolver runs on the request's own flow. A host that sets Activity.Current there (the workaround #170
        // describes for a tap without TraceIdFromIdentity) would otherwise leave the test's trace hanging off its activity.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded);

        var run = await RunAsync(o =>
        {
            o.TraceIdFromIdentity = true;
            o.IdentityResolver = (_, _) =>
            {
                new Activity("host").Start();
                return ("places an order", TestId);
            };
        }, (null, NoHeaders));

        var header = Parse(run.Forwarded.Single()!);
        Assert.Equal(TestId, header.Groups["trace"].Value);
        var forward = listener.Stopped.Single(a => a.SpanId.ToString() == header.Groups["span"].Value);
        var server = listener.Stopped.Single(a => a.Kind == ActivityKind.Server && a.SpanId == forward.ParentSpanId);
        Assert.Equal(default, server.ParentSpanId);
    }

    // ─── The tap's own HttpClient (the #170 audit) ─────────────

    private const string CallersTrace = "0af7651916cd43dd8448eb211c80319c";

    [Theory]
    [InlineData(null)]
    [InlineData("00-" + CallersTrace + "-b7ad6b7169203331-01")]
    public async Task HttpClient_instrumentation_in_the_taps_process_leaves_the_forwarded_traceparent_alone(string? inbound)
    {
        // The tap forwards with an HttpClient of its own, which in 4.19.0 ran whatever HttpClient instrumentation the
        // process had. OpenTelemetry's instrumentation puts a span of its own in place of the traceparent, and with
        // nothing listening to the tap that span is the root of a new trace: neither the caller's trace nor the test's
        // reached the service, while the tap's records named it.
        using var provider = Sdk.CreateTracerProviderBuilder().AddHttpClientInstrumentation().Build();

        var run = await RunAsync(FromIdentity, (inbound, NoHeaders), (inbound, NoHeaders));

        var trace = inbound is null ? TestId : CallersTrace;
        Assert.All(run.Forwarded, f => Assert.Equal(trace, TraceOf(f)));
        Assert.All(run.Logs, l => Assert.Equal(trace, l.ActivityTraceId));
        if (inbound is not null)
            Assert.All(run.Forwarded, f => Assert.Equal(inbound, f));
    }

    [Fact]
    public async Task Under_Kronikols_listener_and_HttpClient_instrumentation_the_forward_stays_sampled_under_the_taps_span()
    {
        // A test process that configures OpenTelemetry has both. 4.19.0 forwarded -00 there, the instrumentation's own
        // span under the tap's unrecorded one put in place of the tap's header, which undid 4.18.3's sampled flag, and
        // the service's spans named a parent no store holds, so internal flow could not tie them to the call.
        using var provider = Sdk.CreateTracerProviderBuilder().AddHttpClientInstrumentation().Build();
        using var internalFlow = new InternalFlowActivityListener();

        var run = await RunAsync(FromIdentity, (null, NoHeaders), ("00-" + CallersTrace + "-b7ad6b7169203331-01", NoHeaders));

        Assert.Equal([TestId, CallersTrace], run.Forwarded.Select(TraceOf));
        Assert.All(run.Forwarded, f => Assert.Equal("01", Parse(f!).Groups["flags"].Value));
        Assert.All(run.Forwarded, f => Assert.Contains(run.Logs, l => l.ActivitySpanId == Parse(f!).Groups["span"].Value));
    }

    [Fact]
    public async Task A_tap_started_under_an_activity_does_not_carry_it_into_the_calls_it_forwards()
    {
        // The tap's accept loop runs in the context the tap was started in, so in 4.19.0 every request ran under the
        // start-up activity: with a listener, a call that brought no trace was forwarded in that activity's trace and with
        // its baggage, the same for every test. 4.19.0 cleared it for a test's trace only.
        using var listener = new TapListener((ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded);
        await using var upstream = new StubUpstream(Ok);
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = upstream.BaseUri, CallerName = "web", ServiceName = "api",
            Sink = new ListSink(), IdentityResolver = (_, _) => ("places an order", TestId),
        });
        var fixture = new Activity("fixture").AddBaggage("fixture", "yes").Start();
        try
        {
            await tap.StartAsync();
            // Still running, as a fixture's activity is while its tests run; only the test's own flow leaves it.
            Activity.Current = null;
            using var client = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null });
            for (var i = 0; i < 2; i++)
            {
                using var response = await client.GetAsync(new Uri(tap.ListenUri, "/i"));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            fixture.Stop();
            Activity.Current = null;
        }

        List<Dictionary<string, string>> seen;
        lock (upstream.Seen)
            seen = upstream.Seen.Select(s => s.Headers).ToList();
        var traces = seen.Select(h => Parse(h["traceparent"]).Groups["trace"].Value).ToArray();
        Assert.DoesNotContain(fixture.TraceId.ToString(), traces);
        Assert.Equal(2, traces.Distinct().Count());
        Assert.All(seen, h => Assert.False(h.ContainsKey("baggage"), "the start-up activity's baggage was forwarded"));
        Assert.All(listener.Stopped.Where(a => a.Kind == ActivityKind.Server), s => Assert.Equal(default, s.ParentSpanId));
    }

    [Fact]
    public async Task The_test_id_header_and_its_fallback_headers_mint_the_trace_too()
    {
        var run = await RunAsync(o => { o.TraceIdFromIdentity = true; o.TestIdHeaderFallbacks.Add("x-scenario-id"); },
            (null, [(TestTrackingHttpHeaders.CurrentTestIdHeader, TestId)]),
            (null, [("x-scenario-id", TestId)]));

        Assert.Equal([TestId, TestId], run.Forwarded.Select(TraceOf));
    }

    [Fact]
    public async Task Unattributed_requests_keep_a_random_trace()
    {
        // A FallbackTestId shaped like a trace id, and the fresh per-request id (Guid "N", shaped like one too).
        var fixedId = await RunAsync(o => { o.TraceIdFromIdentity = true; o.CaptureUnattributedRequests = true; o.FallbackTestId = TestId; },
            (null, NoHeaders), (null, NoHeaders));
        var freshIds = await RunAsync(o => { o.TraceIdFromIdentity = true; o.CaptureUnattributedRequests = true; },
            (null, NoHeaders), (null, NoHeaders));

        foreach (var run in new[] { fixedId, freshIds })
        {
            var traces = run.Forwarded.Select(TraceOf).ToArray();
            Assert.DoesNotContain(TestId, traces);
            Assert.NotEqual(traces[0], traces[1]);
            Assert.NotEmpty(run.Logs);
            Assert.All(run.Logs, l => Assert.NotEqual(l.TestId, l.ActivityTraceId));
            Assert.DoesNotContain(run.Diagnostics, d => d.Kind == DiagnosticKind.OptionNotApplied);
        }
    }

    [Fact]
    public async Task An_ignored_request_keeps_a_random_trace()
    {
        var run = await RunAsync(FromIdentity, (null, [(TestTrackingHttpHeaders.Ignore, "true")]));

        Assert.NotEqual(TestId, TraceOf(run.Forwarded.Single()));
    }

    [Theory]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736")]
    [InlineData("4bf92f35-77b3-4da6-a3ce-929d0e0e4736")]
    [InlineData("0-1001")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e473")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e47360")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e473g")]
    public async Task Ids_that_are_not_trace_ids_keep_a_random_trace_and_are_counted(string id)
    {
        var run = await RunAsync(o => { o.TraceIdFromIdentity = true; o.IdentityResolver = (_, _) => ("places an order", id); },
            (null, NoHeaders), (null, NoHeaders), (null, NoHeaders));

        var traces = run.Forwarded.Select(TraceOf).ToArray();
        Assert.Equal(3, traces.Distinct().Count());
        Assert.DoesNotContain(id.Replace("-", "").ToLowerInvariant(), traces);
        var entry = Assert.Single(run.Diagnostics, d => d.Kind == DiagnosticKind.OptionNotApplied);
        Assert.Contains("did not apply to 3 request(s)", entry.Message, StringComparison.Ordinal);
        // Both say what a trace id is in full: 32 zeros are 32 lower-case hex digits too.
        Assert.Contains("32 lower-case hex digits, not all zero", entry.Message, StringComparison.Ordinal);
        Assert.Contains("32 lower-case hex digits, not all zero",
            Assert.Single(run.LogLines, l => l.Contains("TraceIdFromIdentity", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resolver_that_names_no_id_is_still_forwarded_and_counted()
    {
        // The resolver's id is declared non-null, but a host can hand back null. 4.19.0 read its length and threw, so with
        // the option on the tap answered 502 to every such request, which it forwards with the option off.
        var run = await RunAsync(o => { o.TraceIdFromIdentity = true; o.IdentityResolver = (_, _) => ("places an order", null!); },
            (null, NoHeaders), (null, NoHeaders));

        Assert.Equal(2, run.Forwarded.Select(TraceOf).Distinct().Count());
        var entry = Assert.Single(run.Diagnostics, d => d.Kind == DiagnosticKind.OptionNotApplied);
        Assert.Contains("did not apply to 2 request(s)", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceIdFromIdentity_is_off_by_default_and_changes_nothing_when_off()
    {
        Assert.False(new ProxyTapOptions().TraceIdFromIdentity);

        var run = await RunAsync(o => o.IdentityResolver = (_, _) => ("places an order", TestId), (null, NoHeaders));

        Assert.NotEqual(TestId, TraceOf(run.Forwarded.Single()));
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public async Task With_SynthesizeTraceparent_off_nothing_is_sent_and_the_records_carry_the_test_trace()
    {
        var run = await RunAsync(o => { FromIdentity(o); o.SynthesizeTraceparent = false; }, (null, NoHeaders));

        Assert.Null(run.Forwarded.Single());
        Assert.NotEmpty(run.Logs);
        Assert.All(run.Logs, l => Assert.Equal(TestId, l.ActivityTraceId));
    }

    [Fact]
    public async Task A_second_tap_behind_a_w3c_only_service_files_the_call_under_the_test()
    {
        // The issue's chain in one process: tap A knows the test from its resolver and passes on no test-tracking
        // headers; the service behind it passes on traceparent alone, as OpenTelemetry's default propagators do; tap B,
        // in front of the next service, takes the trace id as the test id.
        await using var downstream = new StubUpstream(Ok);
        var sinkB = new ListSink();
        await using var tapB = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = downstream.BaseUri, CallerName = "api", ServiceName = "rows", Sink = sinkB,
        });
        await tapB.StartAsync();
        using var onward = new HttpClient();
        await using var service = new StubUpstream((request, _) =>
        {
            using var call = new HttpRequestMessage(HttpMethod.Get, new Uri(tapB.ListenUri, "/rows"));
            if (request.Headers["traceparent"] is { } traceparent)
                call.Headers.TryAddWithoutValidation("traceparent", traceparent);
            onward.Send(call).Dispose();
            return (200, Encoding.UTF8.GetBytes("{}"), "application/json", null, null);
        });
        var sinkA = new ListSink();
        await using (var tapA = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = service.BaseUri, CallerName = "app", ServiceName = "api", Sink = sinkA,
            ReinjectCorrelation = false, TraceIdFromIdentity = true, IdentityResolver = (_, _) => ("places an order", TestId),
        }))
        {
            await tapA.StartAsync();
            using var app = new HttpClient();
            using var response = await app.GetAsync(new Uri(tapA.ListenUri, "/orders"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.NotEmpty(sinkB.Logs);
        Assert.All(sinkB.Logs, l => Assert.Equal(TestId, l.TestId));
    }
}
