using Kronikol.Extensions.Grpc;
using Kronikol.InternalFlow;
using Kronikol.Tests.Grpc.Hop;
using Kronikol.Tracking;

namespace Kronikol.Tests.Grpc.MultiHost;

/// <summary>
/// #138: the spans of a gRPC call over a socket, as internal flow draws them (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c>).
/// Each call made through the interceptor is Kronikol's span, grpc-dotnet's <c>GrpcOut</c> under it and HttpClient's
/// request under that. The request's span came from no source, so every gRPC call had a step in an "Unknown" lane: 1,403
/// of them in one report of the issue. Grpc.Net.Client 2.45.0, the floor CI also runs these facts on
/// (<c>-p:FloorRun=true</c>), makes its span with <c>new Activity(...)</c>, with no
/// source, and the report names it from its operation as it does any span made that way.
/// </summary>
public class GrpcCallSpansTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task A_gRPC_call_over_a_socket_leaves_no_span_without_a_source_in_its_trace()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B " + _suffix, o =>
        {
            o.Kestrel = true;
            o.NextHttp = c;
        });
        var client = b.CreateTestClient(new GrpcTrackingOptions
        {
            ServiceName = b.Name,
            CallerName = "Test",
            CurrentTestInfoFetcher = () => ("Place an order", _testId),
        });

        await client.CallAsync(new HopRequest { Text = "hello" }, cancellationToken: TestContext.Current.CancellationToken);

        var call = Assert.Single(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == _testId && l.ServiceName == b.Name && l.Type == RequestResponseType.Request);
        var spans = InternalFlowSpanStore.GetSpans().Where(s => s.TraceId.ToString() == call.ActivityTraceId).ToArray();
        var sources = spans.Select(s => FlowSpan.From(s).Source).ToArray();
        Assert.Contains("Grpc.Net.Client", sources);
        Assert.Contains("System.Net.Http", sources);
        Assert.DoesNotContain("", sources);
    }
}
