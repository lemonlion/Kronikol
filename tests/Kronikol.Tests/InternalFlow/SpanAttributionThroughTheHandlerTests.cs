using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #87 through the real <see cref="TestTrackingMessageHandler"/> (<c>plans/SPAN_ATTRIBUTION_PLAN.md</c> §4.2): two
/// tests call one service at the same time, one request doing three queries and the other five. The service continues
/// the caller's trace as ASP.NET Core does, from the <c>traceparent</c> header or, in process, from the ambient
/// Activity. Every span records the test whose request made it, so a popup can be checked span by span.
/// </summary>
[Collection("DiagramsFetcher")]
public class SpanAttributionThroughTheHandlerTests : IDisposable
{
    private readonly string _sourceName = $"Kronikol.Tests.SpanAttribution.Service.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _spans = new();

    public SpanAttributionThroughTheHandlerTests()
    {
        _source = new ActivitySource(_sourceName);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName,
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
    public async Task Calls_without_an_ambient_Activity_get_a_trace_each_and_every_popup_holds_only_its_own_requests_spans()
    {
        Activity.Current = null;
        var (a, b) = await BothTestsCallTheService();

        var segments = Segments(a, b);

        Assert.Equal(["server", "query", "query", "query"], Own(segments, a).Select(s => s.OperationName));
        Assert.Equal(["server", "query", "query", "query", "query", "query"], Own(segments, b).Select(s => s.OperationName));
        Assert.All(Own(segments, a), s => Assert.Equal(a, s.GetTagItem("test")));
        Assert.All(Own(segments, b), s => Assert.Equal(b, s.GetTagItem("test")));
        Assert.Equal(0, segments.Values.Sum(s => s.SpansLeftOut));
    }

    [Fact]
    public async Task Calls_under_one_ambient_Activity_shared_by_both_tests_leave_out_what_they_share_instead_of_pooling_it()
    {
        Activity.Current = null;
        using (var run = _source.StartActivity("run")!)
        {
            run.SetTag("test", "run");
            var (a, b) = await BothTestsCallTheService();

            var segments = Segments(a, b);

            // Every call records the one trace and the one span, so neither the trace nor the tree tells the two
            // requests apart, and they ran at the same time: a popup may keep a span only if its test's own request
            // made it, and says how many it left out.
            Assert.All(Own(segments, a), s => Assert.Equal(a, s.GetTagItem("test")));
            Assert.All(Own(segments, b), s => Assert.Equal(b, s.GetTagItem("test")));
            Assert.True(segments.Values.All(s => s.SpansLeftOut > 0), "both popups left spans out");
        }
    }

    private async Task<(string A, string B)> BothTestsCallTheService()
    {
        var a = "handler-a-" + Guid.NewGuid().ToString("N");
        var b = "handler-b-" + Guid.NewGuid().ToString("N");
        await Task.WhenAll(Call(a, 3), Call(b, 5));
        return (a, b);
    }

    private async Task Call(string testId, int queries)
    {
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Caller",
            FixedNameForReceivingService = "Orders",
            CurrentTestInfoFetcher = () => ("Concurrent", testId),
        })
        {
            InnerHandler = new Service(_source)
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://orders/api/orders?test={testId}&queries={queries}");
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Dictionary<string, InternalFlowSegment> Segments(string a, string b) =>
        InternalFlowSegmentBuilder.BuildSegments(
            RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == a || l.TestId == b).ToArray(),
            _spans.Where(s => s.DisplayName != "run").ToArray());

    private static Activity[] Own(Dictionary<string, InternalFlowSegment> segments, string testId) =>
        Assert.Single(segments.Values, s => s.TestId == testId).Spans;

    /// <summary>
    /// The service: a server span continuing the caller's trace, and one query span per <c>queries</c>, each long enough
    /// that the two tests' requests are in flight together.
    /// </summary>
    private sealed class Service(ActivitySource source) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var testId = query["test"]!;
            var parent = request.Headers.TryGetValues("traceparent", out var values)
                         && ActivityContext.TryParse(values.First(), null, out var fromHeader)
                ? fromHeader
                : Activity.Current?.Context ?? default;

            using (var server = source.StartActivity("server", ActivityKind.Server, parent)!)
            {
                server.SetTag("test", testId);
                for (var i = 0; i < int.Parse(query["queries"]!); i++)
                {
                    using var span = source.StartActivity("query")!;
                    span.SetTag("test", testId);
                    await Task.Delay(15, cancellationToken);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
