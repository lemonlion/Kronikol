using System.Diagnostics;
using Kronikol.Tracking;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A failed forward's spans, and listeners that throw (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md R1 and R2). Listeners are
/// process-wide, so these facts run in the trace-context facts' collection, alone; each also listens only to a trace of
/// its own, in case a listener outlives its fact.
/// </summary>
[Collection(nameof(ProxyTapTraceContextTests))]
public class ProxyTapFailureSpanTests
{
    private static ActivityListener Listen(string traceId, List<Activity> stopped, string? throwIn = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tap.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (throwIn == "ActivityStarted" && activity.TraceId.ToString() == traceId)
                    throw new InvalidOperationException("listener threw");
            },
            ActivityStopped = activity =>
            {
                if (activity.TraceId.ToString() != traceId)
                    return;
                lock (stopped) stopped.Add(activity);
                if (throwIn == "ActivityStopped")
                    throw new InvalidOperationException("listener threw");
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static (string, string) Traceparent(string traceId) => ("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

    [Fact]
    public async Task A_failed_forward_ends_its_spans_with_an_error_status()
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var stopped = new List<Activity>();
        using var listener = Listen(traceId, stopped);
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using var tap = await ProxyTapFailureTests.StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink);

        var (status, _) = await ProxyTapFailureTests.SendAsync(tap, "t-spans", headers: Traceparent(traceId));

        Assert.Equal(502, status);
        var response = (await ProxyTapFailureTests.RecordsAsync(sink, 2)).Single(r => r.Type == RequestResponseType.Response);
        Activity[] spans;
        lock (stopped) spans = stopped.ToArray();
        var client = Assert.Single(spans, a => a.Kind == ActivityKind.Client);
        var server = Assert.Single(spans, a => a.Kind == ActivityKind.Server);
        Assert.Equal(ActivityStatusCode.Error, client.Status);
        Assert.Equal(response.Error, client.StatusDescription);
        Assert.Equal("connection_error", client.GetTagItem("error.type"));
        Assert.Null(client.GetTagItem("http.response.status_code"));
        Assert.Equal(ActivityStatusCode.Error, server.Status);
        Assert.Equal(502, server.GetTagItem("http.response.status_code"));
    }

    [Theory]
    [InlineData("ActivityStarted")]
    [InlineData("ActivityStopped")]
    public async Task A_throwing_ActivityListener_does_not_fail_the_call(string throwIn)
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        using var listener = Listen(traceId, [], throwIn);
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var sink = new ListSink();
        await using var tap = await ProxyTapFailureTests.StartAsync(upstream.BaseUri, sink);

        var (status, body) = await ProxyTapFailureTests.SendAsync(tap, "t-listener", headers: Traceparent(traceId));

        Assert.Equal((200, "ok"), (status, body));
        Assert.Equal(2, (await ProxyTapFailureTests.RecordsAsync(sink, 2)).Length);
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Contains(tap.Diagnostics(), d => d.Message.Contains("capture fault(s)", StringComparison.Ordinal)
                                                && d.Message.Contains("(the first: InvalidOperationException, listener threw)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_listener_that_throws_leaves_Activity_Current_as_it_was()
    {
        // Activity.Start makes a span current before it tells the listeners, and Stop puts the previous one back only
        // after it has told them, so a listener that throws at both leaves a span current unless the tap restores it.
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var parent = new ActivityContext(ActivityTraceId.CreateFromString(traceId), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);
        var tap = new Tap(new Kronikol.Extensions.ProxyTap.ProxyTapOptions());
        var before = new Activity("before").Start();
        try
        {
            using (Listen(traceId, [], "ActivityStarted"))
            using (var alsoAtStop = Listen(traceId, [], "ActivityStopped"))
            {
                Assert.Null(tap.StartSpan(() => Tap.ActivitySource.StartActivity("span", ActivityKind.Server, parent)));
                Assert.Same(before, Activity.Current);
            }

            using (Listen(traceId, [], "ActivityStopped"))
            {
                var span = tap.StartSpan(() => Tap.ActivitySource.StartActivity("span", ActivityKind.Server, parent));
                Assert.NotNull(span);
                Assert.Same(span, Activity.Current);
                tap.EndSpan(span, restore: before);
                Assert.Same(before, Activity.Current);
                Assert.True(span!.IsStopped);
            }

            Assert.Equal(2, tap.CaptureFaults);
        }
        finally
        {
            Activity.Current = before;
            before.Stop();
        }
    }

    [Fact]
    public async Task A_failed_forward_under_a_listener_that_throws_at_the_stop_is_still_answered_and_recorded()
    {
        var traceId = ActivityTraceId.CreateRandom().ToString();
        using var listener = Listen(traceId, [], "ActivityStopped");
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using var tap = await ProxyTapFailureTests.StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink);

        var (status, _) = await ProxyTapFailureTests.SendAsync(tap, "t-listener-failed", headers: Traceparent(traceId));

        Assert.Equal(502, status);
        var records = await ProxyTapFailureTests.RecordsAsync(sink, 2);
        Assert.Equal("!HttpRequestException", ProxyTapFailureTests.StatusOf(records.Single(r => r.Type == RequestResponseType.Response)));
        Assert.Equal(1, tap.ForwardFailures);
        Assert.True(tap.CaptureFaults >= 1);
    }
}
