using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

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

    /// <summary>
    /// Sends one request per entry through a fresh tap (a null entry sends no <c>traceparent</c>), each naming test
    /// <c>t</c> by header, and returns the <c>traceparent</c> the upstream received for each and the tap's records.
    /// </summary>
    private static async Task<(List<string?> Forwarded, List<RequestResponseLog> Logs)> SendAsync(
        Action<ProxyTapOptions>? configure, params string?[] traceparents)
    {
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "api",
            Sink = sink,
        };
        configure?.Invoke(options);
        await using (var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(options))
        {
            await tap.StartAsync();
            using var client = new HttpClient();
            foreach (var traceparent in traceparents)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/i"));
                request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, "t");
                if (traceparent is not null)
                    request.Headers.TryAddWithoutValidation("traceparent", traceparent);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        lock (upstream.Seen)
            return (upstream.Seen.Select(s => s.Headers.TryGetValue("traceparent", out var v) ? v : null).ToList(), sink.Logs.ToList());
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
}
