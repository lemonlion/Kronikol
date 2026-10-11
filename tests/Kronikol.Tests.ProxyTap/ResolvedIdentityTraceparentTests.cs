using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Reports;
using Kronikol.Tracking;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

[CollectionDefinition(Name, DisableParallelization = true)]
public class ProxyTapActivityCollection
{
    // Facts here attach a process-wide ActivityListener to the tap's source, and the facts without one depend on no
    // listener being attached anywhere in the process, so the collection runs alone.
    public const string Name = "ProxyTap activities";
}

/// <summary>
/// A client that cannot stamp the correlation headers (a native app on a device or an emulator, say) cannot send the test's
/// <c>traceparent</c> either, so a request <see cref="ProxyTapOptions.IdentityResolver"/> attributes arrives with none, and
/// the tap forwarded it in a trace of its own: the services below the tap, which pass on only <c>traceparent</c>, recorded
/// their spans in a trace no test owns. <see cref="ProxyTapOptions.ResolvedIdentityTraceparent"/> lets the host name the
/// test's trace, and the tap continues it as though the caller had sent it.
/// </summary>
/// <remarks>
/// The tap answers the caller before it records, so every fact disposes the tap (which drains the exchange) before it
/// reads the sink or <see cref="Tap.Diagnostics"/>.
/// </remarks>
[Collection(ProxyTapActivityCollection.Name)]
public class ResolvedIdentityTraceparentTests
{
    /// <summary>The test's trace and the span its requests nest under (its root span), as the host knows them.</summary>
    private const string TestTrace = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TestRootSpan = "00f067aa0ba902b7";

    /// <summary>A trace the request itself carries.</summary>
    private const string InboundTrace = "0af7651916cd43dd8448eb211c80319c";
    private const string InboundSpan = "b7ad6b7169203331";

    /// <summary>The header the stand-in device sends instead of the correlation headers; the resolver attributes by it.</summary>
    private const string DeviceHeader = "x-device-serial";

    private static readonly (string Name, string Id) DeviceTest = ("checkout on a device", "checkout-test-7");

    [Fact]
    public async Task With_a_listener_a_request_the_resolver_attributed_nests_under_the_span_the_host_names()
    {
        using var listener = new TapListener();
        var askedFor = new ConcurrentQueue<(string Name, string Id)>();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, identity =>
        {
            askedFor.Enqueue(identity);
            return new TraceParent(TestTrace, TestRootSpan, 1);
        }));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.Equal(DeviceTest, Assert.Single(askedFor)); // asked about the identity the resolver gave
        var server = listener.Server;
        var client = listener.Client;
        Assert.Equal(TestTrace, server.TraceId.ToString());
        Assert.Equal(TestRootSpan, server.ParentSpanId.ToString());
        Assert.Equal(server.SpanId, client.ParentSpanId);
        // The forwarded traceparent is the tap's client span, in the test's trace.
        var forwarded = ForwardedTraceparent(upstream);
        Assert.Equal(client.Id, forwarded);
        Assert.Equal(TestTrace, TraceParent.TryParse(forwarded)!.TraceId);
        var logged = sink.Logs[0];
        Assert.Equal(DeviceTest.Id, logged.TestId);
        Assert.Equal(TestTrace, logged.ActivityTraceId);
        Assert.Equal(client.SpanId.ToString(), logged.ActivitySpanId);
    }

    [Theory]
    [InlineData((byte)1, true)]
    [InlineData((byte)0, true)]
    [InlineData((byte)1, false)]
    [InlineData((byte)0, false)]
    public async Task Without_a_listener_the_request_is_forwarded_with_the_traceparent_the_host_names(byte flags, bool synthesizeTraceparent)
    {
        AssertNoListener();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ => new TraceParent(TestTrace, TestRootSpan, flags),
            o => o.SynthesizeTraceparent = synthesizeTraceparent));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        // Exactly what the caller would have sent had it known the test's trace, so the services below parent their spans on
        // the test's root span. SynthesizeTraceparent is for a request with no trace at all, and this one has one.
        Assert.Equal($"00-{TestTrace}-{TestRootSpan}-{flags:x2}", ForwardedTraceparent(upstream));
        var logged = sink.Logs[0];
        Assert.Equal(DeviceTest.Id, logged.TestId);
        Assert.Equal(TestTrace, logged.ActivityTraceId);
        Assert.Equal(TestRootSpan, logged.ActivitySpanId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_inbound_traceparent_wins_over_the_one_the_host_names(bool withListener)
    {
        using var listener = withListener ? new TapListener() : null;
        if (!withListener)
            AssertNoListener();
        var asked = 0;
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ =>
        {
            Interlocked.Increment(ref asked);
            return new TraceParent(TestTrace, TestRootSpan, 1);
        }));
        await tap.StartAsync();

        var inbound = $"00-{InboundTrace}-{InboundSpan}-01";
        using (var response = await SendFromTheDevice(tap, ("traceparent", inbound)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.Equal(0, asked);
        Assert.Equal(DeviceTest.Id, sink.Logs[0].TestId); // the resolver still attributes it
        Assert.Equal(InboundTrace, sink.Logs[0].ActivityTraceId);
        if (listener is null)
        {
            Assert.Equal(inbound, ForwardedTraceparent(upstream));
        }
        else
        {
            Assert.Equal(InboundTrace, listener.Server.TraceId.ToString());
            Assert.Equal(InboundSpan, listener.Server.ParentSpanId.ToString());
            Assert.Equal(listener.Client.Id, ForwardedTraceparent(upstream));
        }
    }

    [Fact]
    public async Task A_traceparent_the_tap_cannot_read_counts_as_none()
    {
        AssertNoListener();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ => new TraceParent(TestTrace, TestRootSpan, 1)));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap, ("traceparent", "not-a-traceparent")))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.Equal($"00-{TestTrace}-{TestRootSpan}-01", ForwardedTraceparent(upstream));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_identity_read_from_the_headers_never_asks_the_host(bool withListener)
    {
        using var listener = withListener ? new TapListener() : null;
        if (!withListener)
            AssertNoListener();
        var asked = 0;
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ =>
        {
            Interlocked.Increment(ref asked);
            return new TraceParent(TestTrace, TestRootSpan, 1);
        }));
        await tap.StartAsync();

        // No device header, so the resolver answers null and the built-in rules attribute the request by its headers.
        using (var response = await Send(tap,
                   (TestTrackingHttpHeaders.CurrentTestNameHeader, "from the headers"),
                   (TestTrackingHttpHeaders.CurrentTestIdHeader, "header-id")))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.Equal(0, asked);
        Assert.Equal("header-id", sink.Logs[0].TestId);
        AssertOutsideTheTestsTrace(ForwardedTraceparent(upstream));
        Assert.NotEqual(TestTrace, sink.Logs[0].ActivityTraceId);
        if (listener is not null)
            Assert.NotEqual(TestTrace, listener.Server.TraceId.ToString());
    }

    [Theory]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736", TestRootSpan, "the trace id must be 32 lowercase hex digits")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e473", TestRootSpan, "the trace id must be 32 lowercase hex digits")]
    [InlineData(null, TestRootSpan, "the trace id must be 32 lowercase hex digits")]
    [InlineData("00000000000000000000000000000000", TestRootSpan, "the trace id must not be all zeros")]
    [InlineData(TestTrace, "00F067AA0BA902B7", "the parent span id must be 16 lowercase hex digits")]
    [InlineData(TestTrace, "00f067aa0ba902b70", "the parent span id must be 16 lowercase hex digits")]
    [InlineData(TestTrace, "zzf067aa0ba902b7", "the parent span id must be 16 lowercase hex digits")]
    [InlineData(TestTrace, "0000000000000000", "the parent span id must not be all zeros")]
    public async Task A_value_that_is_not_W3C_trace_context_leaves_the_request_in_a_trace_of_its_own_and_is_reported(
        string? traceId, string spanId, string problem)
    {
        AssertNoListener();
        var log = new ConcurrentQueue<string>();
        var named = new TraceParent(traceId!, spanId, 1);
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ => named, o => o.Log = log.Enqueue));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        // As before the option: a traceparent of the tap's own (SynthesizeTraceparent), in a fresh trace.
        AssertOutsideTheTestsTrace(ForwardedTraceparent(upstream));
        Assert.Equal(DeviceTest.Id, sink.Logs[0].TestId); // still attributed: only the trace is lost
        Assert.Equal(1, tap.ResolvedIdentityTraceparentFailures);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("web→graphql: ResolvedIdentityTraceparent gave 1 request(s) no usable trace context", entry.Message);
        Assert.Contains(log, line =>
            line.Contains($"returned {named}, which is not valid W3C trace context ({problem})", StringComparison.Ordinal)
            && line.Contains(DeviceTest.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_a_listener_a_value_that_is_not_W3C_trace_context_leaves_the_tap_span_out_of_the_tests_trace()
    {
        using var listener = new TapListener();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ => new TraceParent(TestTrace, "0000000000000000", 1)));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.NotEqual(TestTrace, listener.Server.TraceId.ToString());
        Assert.Equal(listener.Client.Id, ForwardedTraceparent(upstream));
        Assert.Equal(1, tap.ResolvedIdentityTraceparentFailures);
    }

    [Fact]
    public async Task A_host_callback_that_throws_leaves_the_request_in_a_trace_of_its_own_and_is_reported()
    {
        AssertNoListener();
        var log = new ConcurrentQueue<string>();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ => throw new InvalidOperationException("no root span for this test"),
            o => o.Log = log.Enqueue));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        AssertOutsideTheTestsTrace(ForwardedTraceparent(upstream));
        Assert.Equal(DeviceTest.Id, sink.Logs[0].TestId);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Equal(1, tap.ResolvedIdentityTraceparentFailures);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.StartsWith("web→graphql: ResolvedIdentityTraceparent gave 1 request(s) no usable trace context", entry.Message);
        Assert.Contains(log, line => line.Contains("threw (InvalidOperationException: no root span for this test)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_null_answer_leaves_the_request_as_it_was_and_reports_nothing()
    {
        AssertNoListener();
        var asked = 0;
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(DeviceOptions(upstream, sink, _ =>
        {
            Interlocked.Increment(ref asked);
            return null;
        }));
        await tap.StartAsync();

        using (var response = await SendFromTheDevice(tap))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        Assert.Equal(1, asked);
        AssertOutsideTheTestsTrace(ForwardedTraceparent(upstream));
        Assert.Equal(0, tap.ResolvedIdentityTraceparentFailures);
        Assert.Empty(tap.Diagnostics());
    }

    [Fact]
    public void The_option_needs_an_IdentityResolver()
    {
        var options = new ProxyTapOptions
        {
            ListenPort = 1234,
            ForwardBaseUri = new Uri("http://localhost:1235"),
            CallerName = "web",
            ServiceName = "graphql",
            ResolvedIdentityTraceparent = _ => null,
        };

        var refused = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal(nameof(ProxyTapOptions.ResolvedIdentityTraceparent), refused.ParamName);

        options.IdentityResolver = (_, _) => null;
        options.Validate();
    }

    [Fact]
    public async Task An_IdentityResolver_that_throws_no_longer_fails_the_request()
    {
        var log = new ConcurrentQueue<string>();
        await using var upstream = new StubUpstream(Ok);
        var sink = new ListSink();
        await using var tap = new Tap(Options(upstream, sink, o =>
        {
            o.IdentityResolver = (_, _) => throw new InvalidOperationException("device registry unavailable");
            o.Log = log.Enqueue;
        }));
        await tap.StartAsync();

        using (var response = await Send(tap,
                   (TestTrackingHttpHeaders.CurrentTestNameHeader, "from the headers"),
                   (TestTrackingHttpHeaders.CurrentTestIdHeader, "header-id")))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await tap.DisposeAsync();

        // Forwarded, and attributed by the built-in rules: the headers.
        Assert.Single(upstream.Seen);
        Assert.Equal("header-id", sink.Logs[0].TestId);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Equal(1, tap.IdentityResolverFailures);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("web→graphql: IdentityResolver threw on 1 request(s)", entry.Message);
        Assert.Contains(log, line => line.Contains("IdentityResolver threw (InvalidOperationException: device registry unavailable)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private static (int Status, byte[] Body, string? ContentType, string? ContentEncoding, Dictionary<string, string>? Headers) Ok(HttpListenerRequest _, byte[] __) =>
        (200, Encoding.UTF8.GetBytes("""{"ok":true}"""), "application/json", null, null);

    private static ProxyTapOptions Options(StubUpstream upstream, IRequestResponseSink sink, Action<ProxyTapOptions>? configure = null)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = upstream.BaseUri,
            CallerName = "web",
            ServiceName = "graphql",
            Sink = sink,
        };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>A tap whose resolver attributes every request carrying <see cref="DeviceHeader"/> to <see cref="DeviceTest"/>.</summary>
    private static ProxyTapOptions DeviceOptions(StubUpstream upstream, IRequestResponseSink sink,
        Func<(string Name, string Id), TraceParent?> traceparentFor, Action<ProxyTapOptions>? configure = null) =>
        Options(upstream, sink, o =>
        {
            o.IdentityResolver = (headers, _) => headers.ContainsKey(DeviceHeader) ? DeviceTest : null;
            o.ResolvedIdentityTraceparent = traceparentFor;
            configure?.Invoke(o);
        });

    private static Task<HttpResponseMessage> SendFromTheDevice(Tap tap, params (string Name, string Value)[] headers) =>
        Send(tap, [(DeviceHeader, "emulator-5554"), .. headers]);

    private static async Task<HttpResponseMessage> Send(Tap tap, params (string Name, string Value)[] headers)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/cart"));
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        return await client.SendAsync(request);
    }

    private static string ForwardedTraceparent(StubUpstream upstream)
    {
        var seen = Assert.Single(upstream.Seen);
        Assert.True(seen.Headers.TryGetValue("traceparent", out var traceparent), "The forwarded request carries a traceparent");
        return traceparent!;
    }

    /// <summary>A valid traceparent that is not the test's: the request went on in a trace of its own.</summary>
    private static void AssertOutsideTheTestsTrace(string forwarded)
    {
        var parsed = TraceParent.TryParse(forwarded);
        Assert.NotNull(parsed);
        Assert.NotEqual(TestTrace, parsed!.TraceId);
        Assert.NotEqual(TestRootSpan, parsed.ParentSpanId);
    }

    private static void AssertNoListener() =>
        Assert.False(Tap.ActivitySource.HasListeners(), "A listener is attached to the tap's source, so the no-listener path is not what runs");

    /// <summary>Records the tap's spans for the length of a fact.</summary>
    private sealed class TapListener : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _stopped = new();
        private readonly ActivityListener _listener;

        public TapListener()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Tap.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity Server => _stopped.Single(a => a.Kind == ActivityKind.Server);

        public Activity Client => _stopped.Single(a => a.Kind == ActivityKind.Client);

        public void Dispose() => _listener.Dispose();
    }
}
