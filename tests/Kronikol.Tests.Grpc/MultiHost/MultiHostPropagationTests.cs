using Kronikol.Extensions.Grpc;
using Kronikol.Tests.Grpc.Hop;
using Kronikol.Tracking;
using Microsoft.AspNetCore.TestHost;

namespace Kronikol.Tests.Grpc.MultiHost;

/// <summary>
/// A host called over gRPC attributes its own calls to the scenario (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md
/// section 4.3, T30 to T35), measured on real ASP.NET Core hosts that call <c>AddTestTrackingContextPropagation()</c>,
/// on <see cref="TestServer"/> as a <c>WebApplicationFactory</c> suite runs them, and on Kestrel over h2c. Until this
/// release the interceptor sent only a <c>traceparent</c>, and every one of these calls went unattributed.
/// </summary>
public class MultiHostPropagationTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    private GrpcTrackingOptions TestSideOptions(string service, string name = "Place an order") => new()
    {
        ServiceName = service,
        CallerName = "Test",
        CurrentTestInfoFetcher = () => (name, _testId),
    };

    /// <summary>The call a host made to the stub named <paramref name="stub"/>, as the host's HTTP handler logged it.</summary>
    private static RequestResponseLog? CallTo(string stub) =>
        RequestResponseLogger.RequestAndResponseLogs.SingleOrDefault(l => l.ServiceName == stub && l.Type == RequestResponseType.Request);

    [Fact]
    public async Task A_service_the_test_calls_over_gRPC_attributes_its_own_calls()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B", o => o.NextHttp = c);
        var client = b.CreateTestClient(TestSideOptions("Service B"));

        var reply = await client.CallAsync(new HopRequest { Text = "hello" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_testId, reply.ScopeTestId);
        var call = CallTo(c.Name);
        Assert.NotNull(call);
        Assert.Equal(_testId, call.TestId);
        Assert.Equal("Place an order", call.TestName);
        Assert.Equal(AttributionSource.RequestHeader, call.AttributionSource);
    }

    [Fact]
    public async Task A_host_called_over_gRPC_by_a_host_the_test_called_over_HTTP_attributes_its_own_calls()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B " + _suffix, o => o.NextHttp = c);
        await using var a = await HopHost.StartAsync("Service A " + _suffix, o => o.NextGrpc = b);

        await StartThroughHttp(a);

        var call = CallTo(c.Name);
        Assert.NotNull(call);
        Assert.Equal(_testId, call.TestId);
        Assert.Equal(AttributionSource.RequestHeader, call.AttributionSource);
    }

    [Fact]
    public async Task Two_gRPC_hops_carry_the_identity_to_the_third_host()
    {
        await using var d = await StubHost.StartAsync("Stub D " + _suffix);
        await using var c = await HopHost.StartAsync("Service C " + _suffix, o => o.NextHttp = d);
        await using var b = await HopHost.StartAsync("Service B " + _suffix, o => o.NextGrpc = c);
        var client = b.CreateTestClient(TestSideOptions(b.Name));

        await client.CallAsync(new HopRequest { Text = "hello" }, cancellationToken: TestContext.Current.CancellationToken);

        var call = CallTo(d.Name);
        Assert.NotNull(call);
        Assert.Equal(_testId, call.TestId);
        Assert.Equal(AttributionSource.RequestHeader, call.AttributionSource);
        // The middle hop logged its own gRPC call to C under the scenario too.
        var hop = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.ServiceName == c.Name && l.Type == RequestResponseType.Request);
        Assert.Equal(_testId, hop.TestId);
        Assert.Equal(AttributionSource.RequestHeader, hop.AttributionSource);
    }

    [Fact]
    public async Task A_name_that_is_not_plain_ascii_reaches_a_host_over_h2c_exactly()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B", o =>
        {
            o.Kestrel = true;
            o.NextHttp = c;
        });
        var client = b.CreateTestClient(TestSideOptions("Service B", "Café order ☕"));

        var reply = await client.CallAsync(new HopRequest { Text = "hello" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Café order ☕", reply.ScopeTestName);
        var call = CallTo(c.Name);
        Assert.NotNull(call);
        Assert.Equal("Café order ☕", call.TestName);
        Assert.Equal(_testId, call.TestId);
    }

    [Fact]
    public async Task With_PropagateTestIdentity_off_the_called_host_does_not_attribute_its_calls_to_the_scenario()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B", o => o.NextHttp = c);
        var options = TestSideOptions("Service B");
        options.PropagateTestIdentity = false;
        var client = b.CreateTestClient(options);

        var reply = await client.CallAsync(new HopRequest { Text = "hello" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("", reply.ScopeTestId);
        Assert.Equal("", reply.SeenTestId);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.ServiceName == c.Name && l.TestId == _testId);
    }

    [Fact]
    public async Task The_called_hosts_calls_share_the_gRPC_calls_Kronikol_trace_id()
    {
        await using var c = await StubHost.StartAsync("Stub C " + _suffix);
        await using var b = await HopHost.StartAsync("Service B " + _suffix, o => o.NextHttp = c);
        var bAsACallsIt = "Orders API " + _suffix;
        await using var a = await HopHost.StartAsync("Service A " + _suffix, o =>
        {
            o.NextGrpc = b;
            o.ConfigureNextGrpc = grpc => grpc.ServiceName = bAsACallsIt;
        });

        await StartThroughHttp(a);

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId && l.Type == RequestResponseType.Request).ToArray();
        var testToA = Assert.Single(logs, l => l.ServiceName == a.Name);
        var aToB = Assert.Single(logs, l => l.ServiceName == bAsACallsIt);
        var bToC = Assert.Single(logs, l => l.ServiceName == c.Name);
        // A names B "Orders API" and B names itself "Service B", so CallNesting's first rule (a call nests under an
        // open call whose service is the caller) cannot nest B's call under the gRPC call. Its second rule, a shared
        // Kronikol trace id, does.
        Assert.NotEqual(aToB.ServiceName, bToC.CallerName);
        Assert.Equal(aToB.TraceId, bToC.TraceId);
        Assert.Equal(testToA.TraceId, aToB.TraceId);
    }

    private async Task StartThroughHttp(HopHost a)
    {
        using var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            FixedNameForReceivingService = a.Name,
            CurrentTestInfoFetcher = () => ("Place an order", _testId),
        })
        { InnerHandler = a.App.GetTestServer().CreateHandler() })
        { BaseAddress = a.BaseAddress };

        (await client.GetAsync("/start", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }
}
