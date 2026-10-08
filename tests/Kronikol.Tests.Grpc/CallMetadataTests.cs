using System.Diagnostics;
using System.Text.RegularExpressions;
using Grpc.Core;
using Kronikol.Extensions.Grpc;
using Kronikol.Tests.Grpc.Hop;
using Kronikol.Tests.Grpc.MultiHost;

namespace Kronikol.Tests.Grpc;

/// <summary>
/// The metadata the interceptor sends and the span it leaves behind (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md
/// section 4.2, T1 to T6): the caller's <see cref="Metadata"/> is copied, never written to; a caller's
/// <c>traceparent</c> is kept; the flags follow the span; an awaited unary call gives <c>Activity.Current</c> back.
/// </summary>
public class CallMetadataTests
{
    private const string AnyTraceparent = "00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]";

    private readonly string _testId = Guid.NewGuid().ToString();

    private GrpcTrackingOptions Options() => new()
    {
        ServiceName = "Service B",
        CallerName = "Test",
        CurrentTestInfoFetcher = () => ("Call metadata", _testId),
    };

    // ─── T1: the caller's Metadata ──────────────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_Metadata_reused_for_three_calls_is_left_as_it_was_and_each_call_sends_one_traceparent(CallKind kind)
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var shared = new Metadata { { "authorization", "Bearer token" } };

        for (var call = 1; call <= 3; call++)
        {
            var sent = await CallKinds.InvokeAsync(interceptor, kind, shared);

            Assert.NotSame(shared, sent.Options.Headers);
            Assert.Single(sent.Options.Headers!, e => e.Key == "traceparent");
            Assert.Equal("Bearer token", Assert.Single(sent.Options.Headers!, e => e.Key == "authorization").Value);
        }

        Assert.Equal("authorization", Assert.Single(shared).Key);
    }

    [Fact]
    public async Task A_Metadata_reused_for_three_calls_puts_the_called_hosts_span_in_the_callers_trace_every_time()
    {
        await using var b = await HopHost.StartAsync("Service B");
        var client = b.CreateTestClient(Options());
        var shared = new Metadata { { "authorization", "Bearer token" } };

        for (var call = 1; call <= 3; call++)
        {
            var reply = await client.CallAsync(new HopRequest { Text = $"call {call}" }, shared, cancellationToken: TestContext.Current.CancellationToken);

            var traceparent = Assert.Single(Regex.Matches(reply.SeenTraceparents, AnyTraceparent)).Value;
            Assert.Equal(traceparent.Substring(3, 32), reply.ServerTraceId);
            Assert.Equal(traceparent.Substring(36, 16), reply.ServerParentSpanId);
        }

        Assert.Single(shared);
    }

    // ─── T2: Metadata.Empty ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_call_whose_headers_are_Metadata_Empty_is_sent_with_a_traceparent(CallKind kind)
    {
        var sent = await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind, Metadata.Empty);

        Assert.Matches(AnyTraceparent, Assert.Single(sent.Options.Headers!, e => e.Key == "traceparent").Value);
        Assert.Empty(Metadata.Empty);
    }

    // ─── T3: the caller's own traceparent ───────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task A_traceparent_the_caller_set_is_sent_unchanged_and_no_second_one_is_added(CallKind kind)
    {
        const string callers = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        var sent = await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind, new Metadata { { "traceparent", callers } });

        Assert.Equal(callers, Assert.Single(sent.Options.Headers!, e => e.Key == "traceparent").Value);
    }

    // ─── T4 and T5: the span is given back ──────────────────────

    [Fact]
    public async Task After_an_awaited_unary_call_Activity_Current_is_what_it_was_before()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var before = Activity.Current;
        Activity? during = null;

        await interceptor.AsyncUnaryCall("request", CallKinds.Context(), (_, _) =>
        {
            during = Activity.Current;
            return CallKinds.Unary();
        }).ResponseAsync;

        Assert.Equal("Kronikol.Grpc", during?.Source.Name); // the call ran under the interceptor's span
        Assert.Same(before, Activity.Current);
    }

    [Fact]
    public async Task After_an_awaited_unary_call_an_outer_span_is_current_again()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        using var outer = new Activity("outer").Start();
        Activity? during = null;

        await interceptor.AsyncUnaryCall("request", CallKinds.Context(), (_, _) =>
        {
            during = Activity.Current;
            return CallKinds.Unary();
        }).ResponseAsync;

        Assert.Same(outer, during?.Parent);
        Assert.Same(outer, Activity.Current);
    }

    [Fact]
    public async Task Two_unary_calls_in_a_row_with_no_ambient_span_are_two_traces()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        Metadata? first = null, second = null;

        await interceptor.AsyncUnaryCall("one", CallKinds.Context(), (_, c) => { first = c.Options.Headers; return CallKinds.Unary(); }).ResponseAsync;
        await interceptor.AsyncUnaryCall("two", CallKinds.Context(), (_, c) => { second = c.Options.Headers; return CallKinds.Unary(); }).ResponseAsync;

        Assert.NotEqual(TraceIdOf(first!), TraceIdOf(second!));
    }

    // ─── T6: the flags follow the span ──────────────────────────

    [Fact]
    public async Task The_flags_say_sampled_when_another_listener_records_the_span()
    {
        var method = CallKinds.Method(CallKind.AsyncUnary, "flags.Probe", "Recorded" + Guid.NewGuid().ToString("N"));
        using var recorder = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Kronikol.Grpc",
            // Only this fact's span, so facts running beside it keep Kronikol's own sampling.
            Sample = (ref ActivityCreationOptions<ActivityContext> o) =>
                o.Name == method.FullName ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(recorder);

        var sent = await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), CallKind.AsyncUnary, method: method);

        Assert.EndsWith("-01", Assert.Single(sent.Options.Headers!, e => e.Key == "traceparent").Value);
    }

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public async Task The_flags_say_not_sampled_under_Kronikols_own_listener(CallKind kind)
    {
        var sent = await CallKinds.InvokeAsync(new GrpcTrackingInterceptor(Options()), kind);

        Assert.EndsWith("-00", Assert.Single(sent.Options.Headers!, e => e.Key == "traceparent").Value);
    }

    private static string TraceIdOf(Metadata headers) =>
        Assert.Single(headers, e => e.Key == "traceparent").Value.Substring(3, 32);
}
