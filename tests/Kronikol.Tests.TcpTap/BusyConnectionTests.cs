using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// The Redis decoder pairs a reply with the oldest command it has seen unanswered, so a reply that reaches it before
/// its command shifts every pairing after it on the connection: each command is recorded with the next one's reply,
/// and the last is left unanswered for the reaper to find. Measured before the fix (<see cref="PumpOrderTests"/>):
/// 16 connections of 1,500 GETs each, answered at once by a local stub, recorded 6,785 of 23,991 exchanges with
/// another command's reply and lost the last exchange on 9 of the 16 connections; after it, none and none.
/// </summary>
public class BusyConnectionTests
{
    [Fact]
    public async Task On_busy_connections_every_command_is_recorded_with_its_own_reply()
    {
        const int Connections = 8, PerConnection = 1_000;
        // The stub answers each GET with its own key, so a reply recorded against the wrong command shows.
        await using var server = new StubServer(request =>
        {
            var parts = Encoding.UTF8.GetString(request).Split("\r\n");
            return parts.Length > 4 ? Encoding.UTF8.GetBytes(Resp.Bulk(parts[4])) : null;
        });
        var sink = new RecordingSink();
        await using var tap = new RedisTap(new RedisTapOptions
        {
            ListenPort = 0,
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "redis",
            Sink = sink,
            EmitActivities = false,
            FallbackTestId = "busy",
        });
        await tap.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, Connections).Select(async connection =>
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync("127.0.0.1", tap.BoundPort);
            var stream = client.GetStream();
            var buffer = new byte[64];
            for (var i = 0; i < PerConnection; i++)
            {
                var key = $"c{connection}-{i}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(Resp.Command("GET", key)));
                var expected = Resp.Bulk(key).Length;
                for (var read = 0; read < expected;)
                    read += await stream.ReadAsync(buffer.AsMemory(read));
            }
        }));

        Assert.True(await Wait.UntilAsync(() => sink.Responses.Count >= Connections * PerConnection, 10_000),
            $"{sink.Responses.Count} of {Connections * PerConnection} exchanges were recorded");
        var requests = sink.Requests.ToDictionary(r => r.RequestResponseId);
        var mispaired = sink.Responses
            .Where(r => !(r.Content ?? "").Contains(requests[r.RequestResponseId].Uri.ToString()["redis://db0/".Length..], StringComparison.Ordinal))
            .ToList();
        Assert.True(mispaired.Count == 0, $"{mispaired.Count} exchanges were recorded with another command's reply");
    }
}
