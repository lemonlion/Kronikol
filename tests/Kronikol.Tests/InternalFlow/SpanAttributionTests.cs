using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #87: an internal-flow popup holds the spans of the call it is attached to, never another request's
/// (<c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). Two tests call one endpoint at overlapping times, each doing a known and
/// different number of queries. The trace, then the span tree, then the call's time decide which spans are a call's,
/// and a span that calls of two tests would both take goes to neither: its popup says how many it left out. Before,
/// a call with no trace id took every span of the run in its time, and calls sharing an ambient trace were told apart
/// by time alone, so one request's popup showed ~60 requests' queries.
/// </summary>
public class SpanAttributionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ActivitySource _source = new("Kronikol.Tests.SpanAttribution");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public SpanAttributionTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.SpanAttribution",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    // ─── (a) calls that record no trace id ─────────────────────

    [Fact]
    public void Two_tests_without_trace_ids_keep_their_own_spans_and_leave_out_the_ones_both_could_own()
    {
        // A calls from 0 to 100 ms, B from 90 to 160 ms. A call's time starts 50 ms before its request (a span can
        // start before the request is logged), so 40 to 100 ms is both tests' time.
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 90, 160);
        var aOwn = Spans("a-query", 10, 20, 30);
        var bOwn = Spans("b-query", 110, 120, 130, 140, 150);
        var both = Spans("either", 95, 98);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. aOwn, .. bOwn, .. both]);

        var aSegment = segments[Key(a)];
        var bSegment = segments[Key(b)];
        Assert.Equal(["a-query", "a-query", "a-query"], aSegment.Spans.Select(s => s.OperationName));
        Assert.Equal(Enumerable.Repeat("b-query", 5), bSegment.Spans.Select(s => s.OperationName));
        Assert.Equal(2, aSegment.SpansLeftOut);
        Assert.Equal(2, bSegment.SpansLeftOut);
    }

    [Fact]
    public void A_call_without_a_trace_id_does_not_take_a_span_another_tests_trace_claims()
    {
        var a = Request("test-a", 0, 100);
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var b = Request("test-b", 10, 90, bTrace);
        var bSpans = Spans("b-query", 20, 30, 40, 50, 60, traceId: bTrace);
        var aSpan = Spans("a-query", 70, traceId: null);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. bSpans, .. aSpan]);

        Assert.Equal(["a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(Enumerable.Repeat("b-query", 5), segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(a)].SpansLeftOut);
    }

    [Fact]
    public void The_whole_test_flow_of_a_test_without_trace_ids_holds_only_its_own_calls_spans()
    {
        var a = Request("test-a", 0, 100);
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var b = Request("test-b", 10, 90, bTrace);
        var bSpans = Spans("b-query", 20, 30, traceId: bTrace);
        var aSpan = Spans("a-query", 70, traceId: null);
        var elsewhere = Spans("another-run-span", 500, traceId: null);

        var whole = InternalFlowSegmentBuilder.BuildWholeTestSegments([.. a, .. b], [.. bSpans, .. aSpan, .. elsewhere]);

        Assert.Equal(["a-query"], whole["iflow-test-test-a"].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-query", "b-query"], whole["iflow-test-test-b"].Spans.Select(s => s.OperationName));
    }

    // ─── (b) calls sharing a trace through an ambient Activity ──

    [Fact]
    public void Tests_sharing_one_ambient_span_keep_what_only_they_could_own()
    {
        // One Activity around both tests: every call records its trace and its span.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, ambient);
        var b = Request("test-b", 90, 160, trace, ambient);
        var aServer = Span("a-server", 5, trace, ambient);
        var aQueries = Children("a-query", aServer, 10, 20, 30);
        var bServer = Span("b-server", 105, trace, ambient);
        var bQueries = Children("b-query", bServer, 110, 120, 130, 140, 150);
        var both = Children("either", aServer, 95, 98);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [aServer, .. aQueries, bServer, .. bQueries, .. both]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query", "b-query", "b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(2, segments[Key(a)].SpansLeftOut);
    }

    [Fact]
    public void The_whole_test_flow_of_tests_sharing_one_ambient_span_holds_only_what_their_calls_kept()
    {
        // A trace other tests' calls claim too is not the test's to take whole: its flame chart holds what its calls
        // kept, as their popups do.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, ambient);
        var b = Request("test-b", 90, 160, trace, ambient);
        var aServer = Span("a-server", 5, trace, ambient);
        var aQueries = Children("a-query", aServer, 10, 20, 30);
        var bServer = Span("b-server", 105, trace, ambient);
        var bQueries = Children("b-query", bServer, 110, 120);
        var both = Children("either", aServer, 95, 98);

        var whole = InternalFlowSegmentBuilder.BuildWholeTestSegments([.. a, .. b],
            [aServer, .. aQueries, bServer, .. bQueries, .. both]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], whole["iflow-test-test-a"].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query"], whole["iflow-test-test-b"].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Each_tests_own_ambient_span_under_one_trace_tells_its_calls_apart_by_the_span_tree()
    {
        // A parent Activity shared by the run, and one per test under it: the trace is shared, the spans are not.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var bTest = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aTest);
        var b = Request("test-b", 0, 100, trace, bTest);
        var aServer = Span("a-server", 5, trace, aTest);
        var aQueries = Children("a-query", aServer, 10, 40, 70);
        var bServer = Span("b-server", 6, trace, bTest);
        var bQueries = Children("b-query", bServer, 15, 30, 45, 60, 75);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [aServer, .. aQueries, bServer, .. bQueries]);

        Assert.Equal(["a-server", "a-query", "a-query", "a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query", "b-query", "b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(a)].SpansLeftOut + segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void A_call_without_a_trace_id_takes_what_its_tests_span_tree_holds_not_the_whole_shared_trace()
    {
        // Each test's HTTP call records the shared trace and the test's own span; each test's database call, like
        // every database tracker's, records no trace at all. The database call may take only what the span tree says
        // is its test's: the whole shared trace in its time would contest the other test's spans, and lose them from
        // the popup the tree had given them to.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var bTest = ActivitySpanId.CreateRandom().ToString();
        var aHttp = Request("test-a", 0, 100, trace, aTest);
        var bHttp = Request("test-b", 0, 100, trace, bTest);
        var aDatabase = Request("test-a", 20, 60);
        var bDatabase = Request("test-b", 30, 70);
        var aServer = Span("a-server", 5, trace, aTest);
        var aQueries = Children("a-query", aServer, 25, 40);
        var bServer = Span("b-server", 6, trace, bTest);
        var bQueries = Children("b-query", bServer, 35, 50, 65);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. aHttp, .. bHttp, .. aDatabase, .. bDatabase],
            [aServer, .. aQueries, bServer, .. bQueries]);

        Assert.Equal(["a-server", "a-query", "a-query"], segments[Key(aHttp)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query"], segments[Key(bHttp)].Spans.Select(s => s.OperationName));
        Assert.Equal(["a-server", "a-query", "a-query"], segments[Key(aDatabase)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query", "b-query", "b-query"], segments[Key(bDatabase)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, new[] { aHttp, bHttp, aDatabase, bDatabase }.Sum(call => segments[Key(call)].SpansLeftOut));
    }

    [Fact]
    public void A_trace_only_one_tests_calls_record_keeps_every_span_in_the_calls_time_whatever_the_tree_says()
    {
        // Nothing to tell apart, so nothing changes: a span of the trace that does not descend from the call's span
        // (work the test started beside the call) stays in the popup, as it did before #87's fix.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aTest = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aTest);
        var server = Span("a-server", 10, trace, aTest);
        var beside = Span("a-beside", 20, trace, ActivitySpanId.CreateRandom().ToString());

        var segments = InternalFlowSegmentBuilder.BuildSegments(a, [server, beside]);

        Assert.Equal(["a-server", "a-beside"], segments[Key(a)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Calls_with_traces_of_their_own_are_exact_whatever_overlaps()
    {
        var aTrace = ActivityTraceId.CreateRandom().ToString();
        var bTrace = ActivityTraceId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, aTrace);
        var b = Request("test-b", 0, 100, bTrace);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [.. Spans("a-query", 10, 20, 30, traceId: aTrace), .. Spans("b-query", 10, 20, 30, 40, 50, traceId: bTrace)]);

        Assert.Equal(3, segments[Key(a)].Spans.Length);
        Assert.Equal(5, segments[Key(b)].Spans.Length);
        Assert.Equal(0, segments[Key(a)].SpansLeftOut + segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void Calls_of_one_test_still_share_the_spans_of_the_calls_nested_in_them()
    {
        // The test's call to the service (0 to 200 ms) and the service's call to a database inside it (50 to 100 ms)
        // record no trace: both popups show the query.
        var outer = Request("test-a", 0, 200);
        var inner = Request("test-a", 50, 100);
        var query = Spans("query", 60);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. outer, .. inner], [.. query]);

        Assert.Single(segments[Key(outer)].Spans);
        Assert.Single(segments[Key(inner)].Spans);
    }

    // ─── One test's calls in one trace (#170, F4) ──────────────────────────────────

    [Fact]
    public void Overlapping_calls_of_one_test_in_one_trace_keep_their_own_spans()
    {
        // One trace per test, as a browser test sends or a tap minting the test's trace forwards: two calls that
        // overlap in time, each forwarded under its own span id, which the store does not hold (the tap's client
        // span is in another process). The span tree gives each server span to one call.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aSpan = ActivitySpanId.CreateRandom().ToString();
        var bSpan = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aSpan);
        var b = Request("test-a", 10, 110, trace, bSpan);
        var aServer = Span("a-server", 20, trace, aSpan);
        var bServer = Span("b-server", 25, trace, bSpan);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [aServer, bServer, .. Children("a-query", aServer, 30), .. Children("b-query", bServer, 35)]);

        Assert.Equal(["a-server", "a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(a)].SpansLeftOut + segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void A_nested_call_of_one_test_still_shows_inside_its_outer_call()
    {
        // The outer call's service calls onward inside it, and the nested call is forwarded under the span the
        // service started for that: its spans descend from both calls' span ids, so both popups show them. A span
        // of the outer call's own work is the outer call's, though it starts inside the nested call's time.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var outerSpan = ActivitySpanId.CreateRandom().ToString();
        var outer = Request("test-a", 0, 200, trace, outerSpan);
        var outerServer = Span("outer-server", 10, trace, outerSpan);
        var outerClient = Children("outer-client", outerServer, 40).Single();
        var nested = Request("test-a", 40, 150, trace, outerClient.SpanId.ToString());
        var nestedServer = Span("nested-server", 50, trace, outerClient.SpanId.ToString());

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. outer, .. nested],
            [outerServer, outerClient, nestedServer, .. Children("nested-query", nestedServer, 60), .. Children("outer-side", outerServer, 70)]);

        Assert.Equal(["outer-server", "outer-client", "nested-server", "nested-query", "outer-side"],
            segments[Key(outer)].Spans.Select(s => s.OperationName));
        Assert.Equal(["outer-client", "nested-server", "nested-query"], segments[Key(nested)].Spans.Select(s => s.OperationName));
        Assert.Equal(0, segments[Key(outer)].SpansLeftOut + segments[Key(nested)].SpansLeftOut);
    }

    [Fact]
    public void Spans_that_descend_from_no_call_stay_in_every_overlapping_window()
    {
        // A span whose chain names neither call's span id (in process, its parent was a span no listener kept)
        // cannot be given to either call by the tree, so it stays where its time puts it, as before.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aSpan = ActivitySpanId.CreateRandom().ToString();
        var bSpan = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aSpan);
        var b = Request("test-a", 10, 110, trace, bSpan);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [Span("a-server", 20, trace, aSpan), Span("b-server", 25, trace, bSpan), Span("unlinked", 30, trace, ActivitySpanId.CreateRandom().ToString())]);

        Assert.Equal(["a-server", "unlinked"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server", "unlinked"], segments[Key(b)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void One_tests_calls_under_one_span_id_keep_todays_selection()
    {
        // In process, every call of a test may record the one ambient span it runs under: nothing tells the calls
        // apart by tree, so each keeps every span of the trace in its time.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, ambient);
        var b = Request("test-a", 10, 110, trace, ambient);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [Span("under", 20, trace, ambient), Span("beside", 30, trace, ActivitySpanId.CreateRandom().ToString())]);

        Assert.Equal(["under", "beside"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["under", "beside"], segments[Key(b)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_trace_of_several_tests_keeps_the_rule_of_87_for_a_call_with_nothing_under_its_span()
    {
        // #87's rule decides a trace that calls of several tests share: a call whose span has nothing under it takes
        // the trace's spans in its time, less those another test's calls take too. The one-test rule is not applied
        // on top, so a span under another call of the same test stays in that call's popup as before.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aSecondSpan = ActivitySpanId.CreateRandom().ToString();
        var bSpan = ActivitySpanId.CreateRandom().ToString();
        var aFirst = Request("test-a", 0, 100, trace, ActivitySpanId.CreateRandom().ToString());
        var aSecond = Request("test-a", 10, 110, trace, aSecondSpan);
        var b = Request("test-b", 0, 100, trace, bSpan);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. aFirst, .. aSecond, .. b],
            [Span("a-second-server", 20, trace, aSecondSpan), Span("b-server", 25, trace, bSpan)]);

        Assert.Equal(["a-second-server"], segments[Key(aFirst)].Spans.Select(s => s.OperationName));
        Assert.Equal(1, segments[Key(aFirst)].SpansLeftOut);
        Assert.Equal(["a-second-server"], segments[Key(aSecond)].Spans.Select(s => s.OperationName));
        Assert.Empty(segments[Key(b)].Spans);
        Assert.Equal(1, segments[Key(b)].SpansLeftOut);
    }

    [Fact]
    public void A_call_keeps_the_spans_of_the_calls_made_inside_it_whatever_span_they_recorded()
    {
        // The shape of the Jest and OpenTelemetry capture IngestSpanTests replays: the test's call is recorded under the
        // span of the service's request hook, and the calls the service makes while handling it under its resolver's
        // span, a sibling of the hook's. Those calls ran inside the test's call (made by the service handling it, while
        // it waited), so their spans are its work too; each inner call keeps out the hook's.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var server = Span("server", 5, trace, null);
        var hook = Children("hook", server, 6).Single();
        var resolver = Children("resolver", server, 10).Single();
        var outer = Request("test-a", 0, 100, trace, hook.SpanId.ToString(), service: "Payments", caller: "Test");
        var psp = Request("test-a", 20, 50, trace, resolver.SpanId.ToString(), service: "PSP", caller: "Payments");
        var risk = Request("test-a", 55, 80, trace, resolver.SpanId.ToString(), service: "Risk", caller: "Payments");

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. outer, .. psp, .. risk],
            [server, hook, resolver, .. Children("psp-post", resolver, 25), .. Children("risk-get", resolver, 60)]);

        Assert.Equal(["server", "hook", "resolver", "psp-post", "risk-get"], segments[Key(outer)].Spans.Select(s => s.OperationName));
        Assert.Equal(["server", "resolver", "psp-post"], segments[Key(psp)].Spans.Select(s => s.OperationName));
        Assert.DoesNotContain("hook", segments[Key(risk)].Spans.Select(s => s.OperationName));
        Assert.Contains("risk-get", segments[Key(risk)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_call_keeps_the_spans_of_a_delivery_on_its_kronikol_trace()
    {
        // A message published inside the test's call is delivered broker to consumer: the broker is not the service
        // the test called, but the delivery carries the call's Kronikol trace id, which is how kronikol query flow
        // nests it inside the call. Its handler's spans are the call's work too.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var kronikolTrace = Guid.NewGuid();
        var callSpan = ActivitySpanId.CreateRandom().ToString();
        var deliverySpan = ActivitySpanId.CreateRandom().ToString();
        var call = Request("test-a", 0, 100, trace, callSpan, service: "Orders", caller: "Test", kronikolTrace: kronikolTrace);
        var delivery = Request("test-a", 30, 60, trace, deliverySpan, service: "Worker", caller: "Broker", kronikolTrace: kronikolTrace);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. call, .. delivery],
            [Span("orders-server", 10, trace, callSpan), Span("worker-handler", 40, trace, deliverySpan)]);

        Assert.Equal(["orders-server", "worker-handler"], segments[Key(call)].Spans.Select(s => s.OperationName));
        Assert.Equal(["worker-handler"], segments[Key(delivery)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_call_of_the_test_that_records_no_span_id_keeps_every_span_of_the_trace_in_its_time()
    {
        // A tracker that records the trace but no span of its own gives the tree nothing to tell its call by.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aSpan = ActivitySpanId.CreateRandom().ToString();
        var bSpan = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aSpan);
        var b = Request("test-a", 10, 110, trace, bSpan);
        var c = Request("test-a", 20, 120, trace);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b, .. c],
            [Span("a-server", 30, trace, aSpan), Span("b-server", 35, trace, bSpan)]);

        Assert.Equal(["a-server", "b-server"], segments[Key(c)].Spans.Select(s => s.OperationName));
        Assert.Equal(["a-server"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server"], segments[Key(b)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_call_made_after_another_was_answered_did_not_run_inside_it()
    {
        // A call is open until its answer: the database call the service makes after the test's call was answered is
        // not that call's work, so what the tree gives to it stays out of the answered call's popup, even what started
        // in its time (a job the service began then, which the later call is recorded under).
        var trace = ActivityTraceId.CreateRandom().ToString();
        var callSpan = ActivitySpanId.CreateRandom().ToString();
        var job = Span("job", 35, trace, null);
        var call = Request("test-a", 0, 50, trace, callSpan, service: "Api", caller: "Test");
        var later = Request("test-a", 70, 90, trace, job.SpanId.ToString(), service: "Db", caller: "Api");

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. call, .. later],
            [Span("api-server", 5, trace, callSpan), job, .. Children("job-step", job, 40)]);

        Assert.Equal(["api-server"], segments[Key(call)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public async Task A_call_record_handed_in_twice_does_not_hang_the_builder()
    {
        // Records are told apart by reference. One handed in twice, by a service that calls itself, must not become its
        // own outer call, or the walk up the outer calls would never end.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aSpan = ActivitySpanId.CreateRandom().ToString();
        var bSpan = ActivitySpanId.CreateRandom().ToString();
        var a = Request("test-a", 0, 100, trace, aSpan, service: "Orders", caller: "Orders");
        var b = Request("test-a", 10, 110, trace, bSpan);

        var segments = await Task.Run(() => InternalFlowSegmentBuilder.BuildSegments([.. a, .. b, a[0]],
                [Span("a-server", 20, trace, aSpan), Span("b-server", 25, trace, bSpan)]))
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(["a-server"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-server"], segments[Key(b)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Overlapping_calls_to_one_service_keep_the_database_calls_made_under_their_own_span()
    {
        // #170's own shape: an app's parallel calls through a tap to one service, whose database spans the OTLP tap
        // records as calls of the test, each claiming its own span id. The service made both queries while both calls
        // were open, so the nesting rule alone put each inside the call opened last, and that call's popup showed the
        // other call's query. The span tree says which call each query ran under: b's query is not a's, nor a's b's.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var aForward = ActivitySpanId.CreateRandom().ToString();
        var bForward = ActivitySpanId.CreateRandom().ToString();
        var a = Request("t1", 0, 100, trace, aForward, service: "Api", caller: "App");
        var b = Request("t1", 10, 110, trace, bForward, service: "Api", caller: "App");
        var aQuery = Span("a-query", 30, trace, aForward);
        var bQuery = Span("b-query", 40, trace, bForward);
        var aDb = Request("t1", 30, 35, trace, aQuery.SpanId.ToString(), service: "Shop", caller: "Api");
        var bDb = Request("t1", 40, 45, trace, bQuery.SpanId.ToString(), service: "Shop", caller: "Api");

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b, .. aDb, .. bDb], [aQuery, bQuery]);

        Assert.Equal(["a-query"], segments[Key(a)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-query"], segments[Key(b)].Spans.Select(s => s.OperationName));
        Assert.Equal(["a-query"], segments[Key(aDb)].Spans.Select(s => s.OperationName));
        Assert.Equal(["b-query"], segments[Key(bDb)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_call_keeps_the_spans_of_the_calls_nested_two_deep_inside_it()
    {
        // The test calls the API, the API calls an inner service, and that service the database; none of them records
        // its span under the one above, so only the nesting says whose work the query is. The outer call keeps the
        // work of the calls inside the calls inside it, at any depth.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var (outerSpan, middleSpan, innerSpan) = (NewSpanId(), NewSpanId(), NewSpanId());
        var outer = Request("t1", 0, 100, trace, outerSpan, service: "Api", caller: "Test");
        var middle = Request("t1", 10, 90, trace, middleSpan, service: "Inner", caller: "Api");
        var inner = Request("t1", 20, 80, trace, innerSpan, service: "Db", caller: "Inner");
        var query = Span("query", 30, trace, innerSpan);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. outer, .. middle, .. inner], [query]);

        Assert.Equal(["query"], segments[Key(outer)].Spans.Select(s => s.OperationName));
        Assert.Equal(["query"], segments[Key(middle)].Spans.Select(s => s.OperationName));
        Assert.Equal(["query"], segments[Key(inner)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void A_call_does_not_run_inside_another_that_records_the_same_span()
    {
        // A page's two requests record the page's one span, and the second is made while the first is open. The span
        // tree says nothing about which ran inside which, since the span is neither's alone, and one party's calls
        // never nest, so the work of the call the second request's service made stays with the second request.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var pageSpan = Span("page", 0, trace, null);
        var page = pageSpan.SpanId.ToString();
        var first = Request("t1", 0, 100, trace, page, service: "Web", caller: "Browser");
        var second = Request("t1", 10, 110, trace, page, service: "Web", caller: "Browser");
        var lookupSpan = NewSpanId();
        var lookup = Request("t1", 20, 40, trace, lookupSpan, service: "Stock", caller: "Web");
        var work = Span("stock-query", 25, trace, lookupSpan);

        var segments = InternalFlowSegmentBuilder.BuildSegments([.. first, .. second, .. lookup], [pageSpan, work]);

        Assert.Equal(["page"], segments[Key(first)].Spans.Select(s => s.OperationName));
        Assert.Equal(["page", "stock-query"], segments[Key(second)].Spans.Select(s => s.OperationName));
    }

    [Fact]
    public void Many_calls_of_one_test_in_one_trace_are_told_apart_in_time_that_grows_with_the_calls()
    {
        // 4.18.3 asked, for each span of the trace and each call that recorded a span id above it, whether that call ran
        // inside the one whose popup it built, walking the nesting again every time. A test that made 300 calls under one
        // ambient span (TestTrackingMessageHandler records Activity.Current's), each with a downstream call of its own,
        // took 22 s to build (4.19.0, plan harness bench-f4.cs). Each call's own span ids are worked out once now.
        var trace = ActivityTraceId.CreateRandom().ToString();
        var ambient = Span("test", 0, trace, null).SpanId.ToString();
        var logs = new List<RequestResponseLog>();
        var downstream = new List<RequestResponseLog[]>();
        for (var i = 0; i < 300; i++)
        {
            var at = 20 * i + 1;
            logs.AddRange(Request("t1", at, at + 16, trace, ambient, service: "Api", caller: "Test"));
            var server = Span("api-server", at + 1, trace, ambient);
            var call = Request("t1", at + 2, at + 14, trace, server.SpanId.ToString(), service: "Db", caller: "Api");
            downstream.Add(call);
            logs.AddRange(call);
            Children("work", server, Enumerable.Range(at + 3, 10).ToArray());
        }

        var all = logs.ToArray();
        var spans = _activities.ToArray();
        var segments = InternalFlowSegmentBuilder.BuildSegments(all, spans);

        // The fastest of three runs, against a budget stretched by this machine's load (ContentionScale).
        var stretch = ContentionScale.Measure();
        var elapsed = long.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sw = Stopwatch.StartNew();
            InternalFlowSegmentBuilder.BuildSegments(all, spans);
            elapsed = Math.Min(elapsed, sw.ElapsedMilliseconds);
        }

        // Each downstream call keeps its own server span and work, and none of its neighbours'.
        Assert.All(downstream, call =>
            Assert.Equal(["api-server", .. Enumerable.Repeat("work", 10)], segments[Key(call)].Spans.Select(s => s.OperationName)));
        var budget = 1000 * stretch;
        Assert.True(elapsed < budget, $"the fastest of three runs took {elapsed} ms against {budget:F0} (1,000 stretched {stretch:F2} times by this machine's load)");
    }

    // ─── What the reader sees ──────────────────────────────────

    [Theory]
    [InlineData(false, InternalFlowFlameChartPosition.BehindWithToggle)]
    [InlineData(true, InternalFlowFlameChartPosition.BehindWithToggle)]
    [InlineData(true, InternalFlowFlameChartPosition.Underneath)]
    public void A_popup_that_left_spans_out_says_how_many_and_why(bool flameChart, InternalFlowFlameChartPosition position)
    {
        // The flame chart is on by default, and each of its layouts builds the popup's content anew.
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 90, 160);
        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b],
            [.. Spans("a-query", 10, 20, 30), .. Spans("either", 95, 98)]);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram,
            showFlameChart: flameChart, flameChartPosition: position);

        var aPopup = System.Text.Json.JsonSerializer.Serialize(data[Key(a)]);
        Assert.Contains("Internal Flow (3 spans, 2 left out)", aPopup, StringComparison.Ordinal);
        Assert.Contains("2 spans that started during this call are left out", aPopup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_that_lost_every_span_gets_a_popup_that_says_why_even_where_empty_popups_are_hidden()
    {
        var a = Request("test-a", 0, 100);
        var b = Request("test-b", 0, 100);
        var segments = InternalFlowSegmentBuilder.BuildSegments([.. a, .. b], [.. Spans("either", 10, 20)]);

        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram,
            noDataBehavior: InternalFlowNoDataBehavior.HideLink);

        Assert.Contains("2 spans that started during this call are left out", System.Text.Json.JsonSerializer.Serialize(data[Key(a)]), StringComparison.Ordinal);
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Key(RequestResponseLog[] call) => $"iflow-{call[0].RequestResponseId}";

    private static string NewSpanId() => ActivitySpanId.CreateRandom().ToString();

    /// <summary>A request and its response, <paramref name="fromMs"/> to <paramref name="toMs"/> after T0.</summary>
    private static RequestResponseLog[] Request(string testId, int fromMs, int toMs, string? traceId = null, string? spanId = null,
        string service = "Orders", string caller = "Caller", Guid? kronikolTrace = null)
    {
        var id = Guid.NewGuid();
        RequestResponseLog Log(RequestResponseType type, int ms) =>
            new("Test", testId, HttpMethod.Get, null, new Uri("http://sut/api/orders"), [], service, caller, type,
                kronikolTrace ?? Guid.NewGuid(), id, false)
            { Timestamp = T0.AddMilliseconds(ms), ActivityTraceId = traceId, ActivitySpanId = spanId };
        return [Log(RequestResponseType.Request, fromMs), Log(RequestResponseType.Response, toMs)];
    }

    private Activity[] Spans(string name, params int[] startsMs) => Spans(name, startsMs, traceId: null);

    private Activity[] Spans(string name, int startMs, string? traceId) => Spans(name, [startMs], traceId);

    private Activity[] Spans(string name, int[] startsMs, string? traceId) =>
        startsMs.Select(ms => Span(name, ms, traceId ?? ActivityTraceId.CreateRandom().ToString(), null)).ToArray();

    private Activity[] Spans(string name, int a, int b, string? traceId) => Spans(name, [a, b], traceId);

    private Activity[] Spans(string name, int a, int b, int c, string? traceId) => Spans(name, [a, b, c], traceId);

    private Activity[] Spans(string name, int a, int b, int c, int d, int e, string? traceId) => Spans(name, [a, b, c, d, e], traceId);

    private Activity Span(string name, int startMs, string traceId, string? parentSpanId)
    {
        Activity.Current = null;
        var parent = new ActivityContext(ActivityTraceId.CreateFromString(traceId),
            parentSpanId is null ? default : ActivitySpanId.CreateFromString(parentSpanId), ActivityTraceFlags.Recorded);
        var span = _source.StartActivity(name, ActivityKind.Internal, parent)!;
        span.SetStartTime(T0.UtcDateTime.AddMilliseconds(startMs));
        span.SetEndTime(T0.UtcDateTime.AddMilliseconds(startMs + 5));
        _activities.Add(span);
        return span;
    }

    private Activity[] Children(string name, Activity parent, params int[] startsMs) =>
        startsMs.Select(ms => Span(name, ms, parent.TraceId.ToString(), parent.SpanId.ToString())).ToArray();
}
