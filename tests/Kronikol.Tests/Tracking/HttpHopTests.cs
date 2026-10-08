using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// A chain of hosts over HTTP stays in its scenario (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md F8, Q1 and R3): the
/// handler a host's HTTP client gets from the DI filter has the host's accessor, and it now passes the identity of the
/// request the host is serving on. Measured before as P8: host B's call to C carried no identity and was recorded under
/// no scenario unless A's handler listed the identity headers in HeadersToForward.
/// </summary>
[Collection("DiagramsFetcher")]
public class HttpHopTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task The_third_host_in_an_HTTP_chain_records_its_calls_under_the_scenario()
    {
        // The hosts' server spans exist only when something listens to ASP.NET Core's source, as Kronikol's own
        // listener does once a tracker starts it.
        Kronikol.InternalFlow.InternalFlowActivityListener.EnsureStarted();
        await using var c = await StartHost($"Stub C {_suffix}", next: null);
        await using var b = await StartHost($"Service B {_suffix}", next: c);
        await using var a = await StartHost($"Service A {_suffix}", next: b);

        using var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            FixedNameForReceivingService = a.Name,
            CurrentTestInfoFetcher = () => ("Place an order", _testId),
        })
        { InnerHandler = a.App.GetTestServer().CreateHandler() })
        { BaseAddress = a.App.GetTestServer().BaseAddress };

        (await client.GetAsync("/", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var requests = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.Type == RequestResponseType.Request && l.ServiceName.EndsWith(_suffix, StringComparison.Ordinal))
            .ToArray();
        var testToA = Assert.Single(requests, l => l.ServiceName == a.Name);
        var aToB = Assert.Single(requests, l => l.ServiceName == b.Name);
        var bToC = Assert.Single(requests, l => l.ServiceName == c.Name);
        Assert.All([aToB, bToC], call =>
        {
            Assert.Equal(_testId, call.TestId);
            Assert.Equal("Place an order", call.TestName);
            Assert.Equal(AttributionSource.RequestHeader, call.AttributionSource);
        });
        // One Kronikol trace id across the chain, so kronikol query flow nests each host's calls under the call into it.
        Assert.Equal(testToA.TraceId, aToB.TraceId);
        Assert.Equal(testToA.TraceId, bToC.TraceId);
        // And one W3C trace: each hop carries a traceparent though TestServer's in-memory transport injects none, so
        // every host's server span, and the calls made under it, stay in the test's trace.
        Assert.Equal(testToA.ActivityTraceId, aToB.ActivityTraceId);
        Assert.Equal(testToA.ActivityTraceId, bToC.ActivityTraceId);
    }

    private sealed record Host(string Name, WebApplication App) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    /// <summary>
    /// A host on TestServer that calls <c>AddTestTrackingContextPropagation()</c> and, for every request, calls
    /// <paramref name="next"/> through a <see cref="TestTrackingMessageHandler"/> holding its own accessor, as the
    /// DI filter builds one.
    /// </summary>
    private static async Task<Host> StartHost(string name, Host? next)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTestTrackingContextPropagation();
        var app = builder.Build();
        app.MapGet("/", async (IHttpContextAccessor accessor, CancellationToken cancellationToken) =>
        {
            if (next is null)
                return "ok";
            using var client = new HttpClient(new TestTrackingMessageHandler(
                new TestTrackingMessageHandlerOptions { CallerName = name, FixedNameForReceivingService = next.Name }, accessor)
            { InnerHandler = next.App.GetTestServer().CreateHandler() })
            { BaseAddress = next.App.GetTestServer().BaseAddress };
            (await client.GetAsync("/", cancellationToken)).EnsureSuccessStatusCode();
            return "ok";
        });
        await app.StartAsync();
        return new Host(name, app);
    }
}
