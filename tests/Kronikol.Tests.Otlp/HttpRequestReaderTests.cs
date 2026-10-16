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

    [Fact]
    public async Task A_head_of_one_header_repeated_is_read_in_proportion_to_its_size()
    {
        // Each repeat was joined onto the whole value so far: 16,000 of them in a 64 KiB head built half a gigabyte of
        // strings, before the tap checks its secret.
        var head = new StringBuilder("POST /v1/traces HTTP/1.1\r\nHost: localhost\r\n");
        var repeats = 0;
        for (; head.Length < (64 * 1024) - 16; repeats++)
            head.Append("a:\r\n");
        head.Append("\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(head.ToString()));
        var reader = new HttpRequestReader(stream, 1024);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await reader.ReadHeadAsync(CancellationToken.None); // a memory stream completes every read at once
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(parsed);
        Assert.Equal(string.Join(", ", Enumerable.Repeat("", repeats)), parsed.Headers["a"]);
        Assert.InRange(allocated, 0, 16L * 1024 * 1024);
    }

    [Fact]
    public async Task An_empty_line_before_a_request_line_is_passed_over()
    {
        // RFC 9112 section 2.2: a server reading a request line ignores at least one empty line before it, as a client may
        // send a CRLF after a body. The request starts at its request line.
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("\r\nPOST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"));
        var started = 0;
        var reader = new HttpRequestReader(stream, 1024) { HeadStarted = () => started++ };

        var parsed = await reader.ReadHeadAsync(CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("POST", parsed.Method);
        Assert.Equal(1, started);
    }

    [Theory]
    [InlineData("\r\n", 0)] // the wait stays the kept-alive connection's
    [InlineData("\r\n\r\n", 1)] // one empty line is passed over, not more: a second starts a request, timed as one
    public async Task An_empty_line_alone_starts_no_request(string sent, int starts)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(sent));
        var started = 0;
        var reader = new HttpRequestReader(stream, 1024) { HeadStarted = () => started++ };

        Assert.Null(await reader.ReadHeadAsync(CancellationToken.None));
        Assert.Equal(starts, started);
    }
}
