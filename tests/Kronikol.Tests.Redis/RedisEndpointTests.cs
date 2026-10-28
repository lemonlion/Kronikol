using System.Net;
using Kronikol.Extensions.Redis;
using Kronikol.Tracking;

namespace Kronikol.Tests.Redis;

/// <summary>
/// The address a Raw call is written under names the server the connection reached. <c>DnsEndPoint.ToString()</c> is
/// <c>Unspecified/localhost:6379</c>, its address family first, so until #192 R1 every Raw address of a connection made
/// by host name, the usual kind, read <c>redis://unspecified/localhost:6379/...</c>. The old address is kept for
/// history's rules 6 and 7 (<see cref="RequestResponseLog.RecordedBefore8"/>).
/// </summary>
public class RedisEndpointTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog TrackedGet(EndPoint endpoint)
    {
        var multiplexer = StubConnectionMultiplexer.CreateProxy(endpoint);
        var tracked = RedisTrackingDatabase.Create(StubDatabase.CreateProxy(d => d.Multiplexer = multiplexer), new RedisTrackingDatabaseOptions
        {
            ServiceName = "Redis",
            CallerName = "TestCaller",
            Verbosity = RedisTrackingVerbosity.Raw,
            CurrentTestInfoFetcher = () => ("Redis endpoints", _testId),
        });
        tracked.StringGet("user:42");
        return Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Fact]
    public void A_host_named_endpoint_is_written_as_its_host_and_port()
    {
        var request = TrackedGet(new DnsEndPoint("localhost", 6379));

        Assert.Equal("redis://localhost:6379/0/user:42", request.Uri.ToString());
        var before = Assert.IsType<RecordedBefore>(request.RecordedBefore8);
        Assert.Equal("redis://unspecified/localhost:6379/0/user:42", before.Uri.ToString());
        Assert.Equal(request.Method.Value, before.Method.Value);
    }

    [Fact]
    public void An_address_endpoint_is_written_as_it_was()
    {
        var request = TrackedGet(new IPEndPoint(IPAddress.Loopback, 6379));

        Assert.Equal("redis://127.0.0.1:6379/0/user:42", request.Uri.ToString());
        Assert.Null(request.RecordedBefore8);
    }
}
