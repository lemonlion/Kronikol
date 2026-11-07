using System.Diagnostics;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #138: one step per gRPC call (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c> R3). grpc-dotnet never names its span, so every
/// gRPC step read <c>Grpc.Net.Client.GrpcOut</c>. The HTTP request that carries the call, and grpc-dotnet's span under
/// Kronikol's interceptor's, were each drawn as a step of their own: three steps for one call.
/// </summary>
public class GrpcStepTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ActivitySource _interceptor = new("Kronikol.Grpc");
    private readonly ActivitySource _grpc = new("Grpc.Net.Client");
    private readonly ActivitySource _http = new("System.Net.Http");
    private readonly ActivitySource _app = new("Kronikol.Tests.GrpcSteps");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public GrpcStepTests()
    {
        _listener = new ActivityListener
        {
            // By reference: the process's own sources of these names are left alone.
            ShouldListenTo = s => ReferenceEquals(s, _interceptor) || ReferenceEquals(s, _grpc) || ReferenceEquals(s, _http)
                                  || ReferenceEquals(s, _app),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _interceptor.Dispose();
        _grpc.Dispose();
        _http.Dispose();
        _app.Dispose();
    }

    // ── The label ──

    [Fact]
    public void A_gRPC_client_span_nobody_named_reads_as_its_method()
    {
        var call = GrpcOut(null, "/orders.Orders/Place");

        Assert.Equal("/orders.Orders/Place", FlowSpan.From(call).Name);
    }

    [Fact]
    public void A_span_someone_named_keeps_its_name()
    {
        var call = GrpcOut(null, "/orders.Orders/Place");
        call.DisplayName = "place the order";

        Assert.Equal("place the order", FlowSpan.From(call).Name);
    }

    [Fact]
    public void A_span_with_OpenTelemetrys_rpc_tags_reads_as_its_service_and_method()
    {
        var call = Start(_app, "Rpc.Call", null);
        call.SetTag("rpc.service", "orders.Orders");
        call.SetTag("rpc.method", "Place");

        Assert.Equal("orders.Orders/Place", FlowSpan.From(call).Name);
    }

    [Fact]
    public void A_span_with_neither_tag_keeps_its_operation_name()
    {
        var call = Start(_grpc, "Grpc.Net.Client.GrpcOut", null);

        Assert.Equal("Grpc.Net.Client.GrpcOut", FlowSpan.From(call).Name);
    }

    // ── The fold ──

    [Fact]
    public void The_HTTP_request_that_carries_a_gRPC_call_folds_into_it()
    {
        var call = GrpcOut(null, "/orders.Orders/Place");
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST");

        var step = Assert.Single(Tree());

        Assert.Equal("/orders.Orders/Place", step.Span.Name);
        Assert.Empty(step.Children);
    }

    [Fact]
    public void A_gRPC_call_that_sent_two_requests_keeps_both()
    {
        // A call that retried or hedged: each attempt is information.
        var call = GrpcOut(null, "/orders.Orders/Place");
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST", startMs: 1);
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST", startMs: 3);

        var step = Assert.Single(Tree());

        Assert.Equal(2, step.Children.Count);
    }

    [Fact]
    public void A_request_with_spans_under_it_folds_and_they_move_up_to_the_call()
    {
        // A host in the test's process answers the call: its server span hangs under the request that carried the call.
        var call = GrpcOut(null, "/orders.Orders/Place");
        var request = Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST");
        Start(_app, "server /orders.Orders/Place", request, ActivityKind.Server, startMs: 1, durationMs: 3);

        var step = Assert.Single(Tree());

        var server = Assert.Single(step.Children);
        Assert.Equal("server /orders.Orders/Place", server.Span.Name);
    }

    [Fact]
    public void Kronikols_span_the_clients_span_and_the_request_are_one_step()
    {
        var method = Start(_interceptor, "/orders.Orders/Place", null, ActivityKind.Internal);
        var call = GrpcOut(method, "/orders.Orders/Place");
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST");

        var step = Assert.Single(Tree());

        Assert.Equal("Kronikol.Grpc", step.Span.Source);
        Assert.Equal("/orders.Orders/Place", step.Span.Name);
        Assert.Empty(step.Children);
    }

    [Fact]
    public void The_clients_span_folds_beside_the_servers_span_that_Kronikols_span_holds()
    {
        // Kronikol's interceptor sends its own span as the call's parent, so a host in the test's process hangs its request
        // span under Kronikol's span, beside grpc-dotnet's. The client's span is the only child of its source, and folds.
        var method = Start(_interceptor, "/orders.Orders/Place", null, ActivityKind.Internal, durationMs: 9);
        var call = GrpcOut(method, "/orders.Orders/Place");
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST");
        Start(_app, "HandlePlace", method, ActivityKind.Server, startMs: 2);

        var step = Assert.Single(Tree());

        Assert.Equal("Kronikol.Grpc", step.Span.Source);
        Assert.Equal("HandlePlace", Assert.Single(step.Children).Span.Name);
    }

    [Fact]
    public void Two_client_spans_beside_the_servers_span_are_both_kept()
    {
        var method = Start(_interceptor, "/orders.Orders/Place", null, ActivityKind.Internal, durationMs: 9);
        GrpcOut(method, "/orders.Orders/Place");
        GrpcOut(method, "/orders.Orders/Place");
        Start(_app, "HandlePlace", method, ActivityKind.Server, startMs: 2);

        var step = Assert.Single(Tree());

        Assert.Equal(["Grpc.Net.Client", "Grpc.Net.Client", "Kronikol.Tests.GrpcSteps"],
            step.Children.Select(c => c.Span.Source).Order());
    }

    [Fact]
    public void A_request_under_a_span_of_another_source_does_not_fold()
    {
        var send = Start(_app, "SendOrder", null, ActivityKind.Internal);
        Start(_http, "System.Net.Http.HttpRequestOut", send, displayName: "POST");

        var step = Assert.Single(Tree());

        Assert.Single(step.Children);
    }

    [Fact]
    public void Every_view_draws_the_call_as_one_step()
    {
        var method = Start(_interceptor, "/orders.Orders/Place", null, ActivityKind.Internal);
        var call = GrpcOut(method, "/orders.Orders/Place");
        Start(_http, "System.Net.Http.HttpRequestOut", call, displayName: "POST");
        var segment = new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "test-1", T0, T0.AddMilliseconds(5),
            _activities.ToArray());

        var diagram = InternalFlowRenderer.RenderActivityDiagram(segment);
        var tree = InternalFlowRenderer.RenderCallTree(segment);
        var flame = InternalFlowRenderer.GetFlameChartData(segment);

        Assert.Contains(":/orders.Orders/Place (5ms);", diagram);
        Assert.DoesNotContain("POST", diagram);
        Assert.DoesNotContain("GrpcOut", diagram);
        Assert.Single(tree.Split("<li").Skip(1));
        Assert.Single(flame.Spans);
    }

    private List<InternalFlowRenderer.SpanNode> Tree() =>
        InternalFlowRenderer.BuildSpanTree(_activities.Select(FlowSpan.From).ToArray());

    private Activity GrpcOut(Activity? parent, string method)
    {
        var call = Start(_grpc, "Grpc.Net.Client.GrpcOut", parent);
        call.SetTag("grpc.method", method);
        return call;
    }

    private Activity Start(ActivitySource source, string name, Activity? parent, ActivityKind kind = ActivityKind.Client,
        string? displayName = null, int startMs = 0, int durationMs = 5)
    {
        Activity.Current = null;
        var context = parent is null ? default : new ActivityContext(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
        var span = source.StartActivity(name, kind, context)!;
        if (displayName is not null)
            span.DisplayName = displayName;
        span.SetStartTime(T0.AddMilliseconds(startMs));
        span.SetEndTime(T0.AddMilliseconds(startMs + durationMs));
        Activity.Current = null;
        _activities.Add(span);
        return span;
    }
}
