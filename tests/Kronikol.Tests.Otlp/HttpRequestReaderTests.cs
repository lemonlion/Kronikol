using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// The reader holds what has arrived of a body, not what its head declares (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6
/// item 3). It made an array of the declared length before the first body byte, so a head declaring
/// <see cref="OtlpTapOptions.MaxRequestBytes"/> and a stall held 32 MiB per connection.
/// </summary>
public class HttpRequestReaderTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_declared_length_is_not_allocated_before_it_arrives()
    {
        const int declared = 32 * 1024 * 1024;
        using var listener = LoopbackPort.Listening();
        using var exporter = new TcpClient();
        await exporter.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var served = await listener.AcceptSocketAsync();
        await using var stream = new NetworkStream(served, ownsSocket: false);
        var reader = new HttpRequestReader(stream, declared);

        await exporter.Client.SendAsync(Encoding.ASCII.GetBytes(
            $"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Length: {declared}\r\n\r\n0123456789"));
        var reading = reader.ReadAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow + Patience;
        while (reader.BodyBytesHeld == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await Task.Delay(200); // the rest of the body is not coming yet
        Assert.False(reading.IsCompleted);
        Assert.InRange(reader.BodyBytesHeld, 1, 1024 * 1024 - 1);

        var rest = new byte[declared - 10];
        rest.AsSpan().Fill((byte)'r');
        await exporter.Client.SendAsync(rest);
        var request = await reading.WaitAsync(Patience);

        Assert.NotNull(request);
        Assert.Equal(declared, request.Body.Length);
        Assert.Equal("0123456789"u8.ToArray(), request.Body[..10]);
        Assert.True(request.Body.AsSpan(10).IndexOfAnyExcept((byte)'r') < 0, "the body that arrived is not the one sent");
    }
}
