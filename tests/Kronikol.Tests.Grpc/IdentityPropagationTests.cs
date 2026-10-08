using Grpc.Core;
using Kronikol.Constants;
using Kronikol.Extensions.Grpc;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Tests.Grpc;

/// <summary>
/// The identity in each call's metadata (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.3, T20 to T29): the four
/// headers the HTTP handler sends, on every hop, only for an identity that names a scenario, whether or not the call
/// is tracked in the current phase, and never in the logged headers.
/// </summary>
public class IdentityPropagationTests
{
    private const string NameKey = TestTrackingHttpHeaders.CurrentTestNameHeader;
    private const string IdKey = TestTrackingHttpHeaders.CurrentTestIdHeader;
    private const string TraceIdKey = TestTrackingHttpHeaders.TraceIdHeader;
    private const string CallerKey = TestTrackingHttpHeaders.CallerNameHeader;
    private static readonly string[] IdentityKeys = [NameKey, IdKey, TraceIdKey, CallerKey];

    private readonly string _testId = "Ns.Klass.Prüfung." + Guid.NewGuid().ToString("N");

    private GrpcTrackingOptions Options(Action<GrpcTrackingOptions>? configure = null)
    {
        var options = new GrpcTrackingOptions
        {
            ServiceName = "Service B",
            CallerName = "Service A",
            CurrentTestInfoFetcher = () => ("Café order", _testId),
        };
        configure?.Invoke(options);
        return options;
    }

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private static string Single(Metadata headers, string key) => Assert.Single(headers, e => e.Key == key).Value;

    // ─── T20: the four keys ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task Each_call_carries_the_four_identity_headers(CallKind kind)
    {
        var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind)).Options.Headers!;

        Assert.Equal("UTF-8''Caf%C3%A9%20order", Single(sent, NameKey));
        Assert.Equal("UTF-8''" + Uri.EscapeDataString(_testId), Single(sent, IdKey));
        Assert.True(Guid.TryParse(Single(sent, TraceIdKey), out _));
        Assert.Equal("Service A", Single(sent, CallerKey));
    }

    // ─── T21 and T22: every hop re-propagates ───────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_host_passes_on_the_identity_of_the_request_it_is_serving(CallKind kind)
    {
        var inboundTraceId = Guid.NewGuid();
        var accessor = new StubAccessor(Inbound("UTF-8''Caf%C3%A9%20order", "UTF-8''Ns.Klass.Pr%C3%BCfung", inboundTraceId.ToString()));
        var interceptor = new GrpcTrackingInterceptor(Options(o => o.CurrentTestInfoFetcher = null), accessor);

        var sent = (await CallKinds.InvokeAsync(interceptor, kind)).Options.Headers!;

        Assert.Equal("UTF-8''Caf%C3%A9%20order", Single(sent, NameKey));
        Assert.Equal("UTF-8''Ns.Klass.Pr%C3%BCfung", Single(sent, IdKey));
        Assert.Equal(inboundTraceId.ToString(), Single(sent, TraceIdKey));
        Assert.Equal("Service A", Single(sent, CallerKey));
    }

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_host_with_no_accessor_passes_on_the_identity_its_middleware_put_in_scope(CallKind kind)
    {
        var interceptor = new GrpcTrackingInterceptor(Options(o => o.CurrentTestInfoFetcher = null));

        Metadata sent;
        using (TestIdentityScope.Begin("Scoped scenario", _testId))
            sent = (await CallKinds.InvokeAsync(interceptor, kind)).Options.Headers ?? new Metadata();

        Assert.Equal("Scoped scenario", Single(sent, NameKey));
        Assert.Equal("UTF-8''" + Uri.EscapeDataString(_testId), Single(sent, IdKey));
    }

    // ─── T23: the option ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task With_PropagateTestIdentity_off_no_identity_header_is_sent_and_the_traceparent_still_is(CallKind kind)
    {
        var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options(o => o.PropagateTestIdentity = false)), kind)).Options.Headers!;

        Assert.DoesNotContain(sent, e => IdentityKeys.Contains(e.Key));
        Assert.Single(sent, e => e.Key == "traceparent");
    }

    [Fact]
    public void PropagateTestIdentity_is_on_by_default()
    {
        Assert.True(new GrpcTrackingOptions().PropagateTestIdentity);
    }

    // ─── T25: a caller's own key wins ───────────────────────────

    public static TheoryData<CallKind, string> KindsAndIdentityKeys
    {
        get
        {
            var data = new TheoryData<CallKind, string>();
            foreach (var kind in Enum.GetValues<CallKind>())
                foreach (var key in IdentityKeys)
                    data.Add(kind, key);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KindsAndIdentityKeys))]
    public async Task A_key_the_caller_set_is_kept_and_not_doubled(CallKind kind, string key)
    {
        // Each key on its own: the caller's value travels alone, and the interceptor still adds the other three.
        var callers = new Metadata { { key, "callers-own-value" } };

        var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind, callers)).Options.Headers!;

        Assert.Equal("callers-own-value", Single(sent, key));
        Assert.All(IdentityKeys.Where(k => k != key), other => Assert.Single(sent, e => e.Key == other));
        Assert.Equal("callers-own-value", Single(callers, key));
    }

    // ─── T26 and T27: the trace id, and what is logged ──────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task The_call_is_logged_with_the_inbound_trace_id_it_sends(CallKind kind)
    {
        var inboundTraceId = Guid.NewGuid();
        var accessor = new StubAccessor(Inbound("Place an order", _testId, inboundTraceId.ToString()));

        var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options(), accessor), kind)).Options.Headers!;

        Assert.Equal(inboundTraceId.ToString(), Single(sent, TraceIdKey));
        Assert.All(Logs(), log => Assert.Equal(inboundTraceId, log.TraceId));
        Assert.NotEmpty(Logs());
    }

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_call_with_no_inbound_trace_id_is_logged_with_the_one_it_sends(CallKind kind)
    {
        var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind)).Options.Headers!;

        var sentTraceId = Guid.Parse(Single(sent, TraceIdKey));
        Assert.All(Logs(), log => Assert.Equal(sentTraceId, log.TraceId));
        Assert.NotEmpty(Logs());
    }

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task The_logged_request_headers_are_the_callers_alone(CallKind kind)
    {
        await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind, new Metadata { { "authorization", "Bearer token" } });

        var request = Assert.Single(Logs(), l => l.Type == RequestResponseType.Request);
        Assert.Equal([("authorization", "Bearer token")], request.Headers.Select(h => (h.Key, h.Value)));
    }

    // ─── T28: an untracked phase still carries the identity ─────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_call_not_tracked_in_the_action_phase_still_carries_the_identity(CallKind kind)
    {
        var interceptor = new GrpcTrackingInterceptor(Options(o => o.TrackDuringAction = false));

        TestPhaseContext.Current = TestPhase.Action;
        Metadata sent;
        try
        {
            sent = (await CallKinds.InvokeAsync(interceptor, kind)).Options.Headers ?? new Metadata();
        }
        finally
        {
            TestPhaseContext.Reset();
        }

        Assert.Empty(Logs());
        Assert.Equal("UTF-8''Caf%C3%A9%20order", Single(sent, NameKey));
        Assert.Equal("UTF-8''" + Uri.EscapeDataString(_testId), Single(sent, IdKey));
    }

    // ─── T29: HasHttpContextAccessor ────────────────────────────

    [Fact]
    public void HasHttpContextAccessor_reports_the_accessor_from_the_constructor_or_the_options()
    {
        Assert.True(new GrpcTrackingInterceptor(Options(), new HttpContextAccessor()).HasHttpContextAccessor);
        Assert.True(new GrpcTrackingInterceptor(Options(o => o.HttpContextAccessor = new HttpContextAccessor())).HasHttpContextAccessor);
        Assert.False(new GrpcTrackingInterceptor(Options()).HasHttpContextAccessor);
    }

    // ─── helpers ────────────────────────────────────────────────

    private static DefaultHttpContext Inbound(string name, string id, string traceId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[NameKey] = name;
        context.Request.Headers[IdKey] = id;
        context.Request.Headers[TraceIdKey] = traceId;
        return context;
    }

    private sealed class StubAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}

/// <summary>
/// T24: what is not sent. These turn <see cref="RequestResponseLogger.CaptureBackground"/> on, which is process-wide,
/// so they run alone.
/// </summary>
[Collection(nameof(BackgroundCaptureCollection))]
public class IdentityPropagationGuardTests
{
    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task The_background_identity_is_not_sent(CallKind kind)
    {
        var options = new GrpcTrackingOptions { ServiceName = "Service B", CallerName = "Service A" };
        var before = RequestResponseLogger.CaptureBackground;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            var sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(options), kind)).Options.Headers!;

            Assert.DoesNotContain(sent, e => e.Key.StartsWith("test-tracking-", StringComparison.Ordinal));
            Assert.Single(sent, e => e.Key == "traceparent"); // the call itself is tracked, as background
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = before;
        }
    }

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_detached_flow_sends_no_identity(CallKind kind)
    {
        var options = new GrpcTrackingOptions
        {
            ServiceName = "Service B",
            CallerName = "Service A",
            CurrentTestInfoFetcher = () => ("A scenario", Guid.NewGuid().ToString()),
        };
        var before = RequestResponseLogger.CaptureBackground;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            Metadata sent;
            using (TestIdentityScope.Detach())
                sent = (await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(options), kind)).Options.Headers!;

            Assert.DoesNotContain(sent, e => e.Key.StartsWith("test-tracking-", StringComparison.Ordinal));
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = before;
        }
    }
}

[CollectionDefinition(nameof(BackgroundCaptureCollection), DisableParallelization = true)]
public class BackgroundCaptureCollection;
