using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kronikol.Extensions.TcpTap;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// Both directions of a connection feed one decode queue, and the decoder pairs a reply with the command it saw
/// before it. A pump that forwarded its bytes before queueing them let a fast reply overtake its command: the
/// server can answer as soon as the command reaches it, and the other pump could queue that answer while this one
/// had not yet queued the command. The decoder then read an answer to nothing and a command nothing answered, so
/// the reaper closed a healthy connection and the capture lost the exchange.
/// </summary>
/// <remarks>
/// CI lost it twice, on commits that did not touch the tap: <c>AnIdleConnectionWithNothingUnansweredIsNeverReaped</c>
/// reaped one connection where none may be (2026-09-13), and <c>TheNdjsonSinkWritesReplayableRecords</c> waited
/// for two lines and got fewer (2026-09-15). The window is a few instructions wide, so it is pinned where it opens
/// rather than raced through sockets: a read is queued before it is forwarded, and the far side cannot answer
/// bytes it has not been sent.
/// </remarks>
public class PumpOrderTests
{
    [Theory]
    [InlineData(TapDirection.ClientToServer)]
    [InlineData(TapDirection.ServerToClient)]
    public async Task A_read_is_queued_for_decoding_before_it_is_forwarded(TapDirection direction)
    {
        var bytes = Encoding.ASCII.GetBytes(Resp.Command("PING"));
        var queue = Channel.CreateUnbounded<TcpTapCore.TapSegment>();
        var destination = new ForwardRecorder(queue.Reader);

        await using var tap = new RedisTap(new RedisTapOptions { ForwardHost = "127.0.0.1", ForwardPort = 1 });
        using var unconnected = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await tap.PumpAsync(new MemoryStream(bytes), destination, unconnected, direction, queue.Writer,
            new TcpTapCore.ConnectionPumpState(1), CancellationToken.None);

        Assert.Equal(bytes, destination.Forwarded.ToArray());
        Assert.True(destination.EveryForwardWasAlreadyQueued,
            "bytes were forwarded before they were queued for decoding, so the reply they cause could be decoded first");
        Assert.True(queue.Reader.TryRead(out var segment));
        Assert.Equal(direction, segment.Direction);
        Assert.Equal(bytes, segment.Data);
    }

    /// <summary>A destination that notes, at each write, whether the queue already holds what it is given.</summary>
    private sealed class ForwardRecorder(ChannelReader<TcpTapCore.TapSegment> queue) : Stream
    {
        private int _writes;

        public MemoryStream Forwarded { get; } = new();
        public bool EveryForwardWasAlreadyQueued { get; private set; } = true;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EveryForwardWasAlreadyQueued &= queue.Count > _writes;
            _writes++;
            Forwarded.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
