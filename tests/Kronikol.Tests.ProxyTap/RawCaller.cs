using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A caller on a raw socket, for what an HTTP client will not do on purpose: stop sending in the middle of a body, stop
/// reading an answer, or read one slowly (plans/TAP_CALLER_TIME_LIMITS_PLAN.md). A reset here is an RST alone: a zero
/// linger, then the socket's own <c>Dispose()</c>, since <see cref="TcpClient.Dispose()"/> sends a FIN first.
/// </summary>
internal sealed class RawCaller : IDisposable
{
    private RawCaller(TcpClient client) => Client = client;

    public TcpClient Client { get; }

    /// <summary>Connects to the tap's port on <c>localhost</c> and sends <paramref name="request"/> as it is.</summary>
    public static async Task<RawCaller> SendAsync(int port, string request, int? receiveBuffer = null)
    {
        var client = new TcpClient();
        if (receiveBuffer is { } size)
            client.ReceiveBufferSize = size;
        await client.ConnectAsync("localhost", port);
        await client.Client.SendAsync(Encoding.ASCII.GetBytes(request));
        return new RawCaller(client);
    }

    /// <summary>Everything the caller hears until the tap lets it go (an end of stream or a reset), or until <paramref name="patience"/> runs out.</summary>
    public async Task<Heard> HearAsync(TimeSpan patience)
    {
        var heard = new List<byte>();
        var buffer = new byte[64 * 1024];
        using var hearing = new CancellationTokenSource(patience);
        try
        {
            while (true)
            {
                var read = await Client.Client.ReceiveAsync(buffer, SocketFlags.None, hearing.Token);
                if (read == 0)
                    return new Heard([.. heard], "an end of stream");
                heard.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            return new Heard([.. heard], $"nothing more within {patience.TotalSeconds:0.#} s");
        }
        catch (SocketException ex)
        {
            return new Heard([.. heard], $"a reset ({ex.SocketErrorCode})");
        }
    }

    /// <summary>An RST and nothing else.</summary>
    public void Reset()
    {
        Client.Client.LingerState = new LingerOption(true, 0);
        Client.Client.Dispose();
    }

    public void Dispose() => Client.Dispose();
}

/// <summary>What a <see cref="RawCaller"/> heard, and how its connection ended.</summary>
internal sealed record Heard(byte[] Bytes, string Ending)
{
    private string Text => Encoding.ASCII.GetString(Bytes);

    /// <summary>The status line, or how the connection ended when the caller heard nothing.</summary>
    public string FirstLine
    {
        get
        {
            var end = Text.IndexOf("\r\n", StringComparison.Ordinal);
            return Bytes.Length == 0 ? Ending : end < 0 ? Text : Text[..end];
        }
    }

    /// <summary>Whether the tap let the caller go: an end of stream or a reset, not silence.</summary>
    public bool LetGo => !Ending.StartsWith("nothing", StringComparison.Ordinal);

    /// <summary>The head, without the blank line that ends it; empty when no whole head arrived.</summary>
    public string Head
    {
        get
        {
            var end = Text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return end < 0 ? "" : Text[..end];
        }
    }

    /// <summary>The bytes after the head.</summary>
    public byte[] Body
    {
        get
        {
            var end = Text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return end < 0 ? [] : Bytes[(end + 4)..];
        }
    }

    public override string ToString() => $"{FirstLine} ({Bytes.Length:N0} bytes, then {Ending})";
}

/// <summary>An upstream on a raw socket that reads a request's head, says so, and never answers.</summary>
internal sealed class HeldUpstream : IDisposable
{
    private readonly TcpListener _listener = LoopbackPort.Listening();
    private readonly List<Socket> _held = [];

    public HeldUpstream()
    {
        BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var socket = await _listener.AcceptSocketAsync();
                    lock (_held) _held.Add(socket);
                    var seen = new StringBuilder();
                    var buffer = new byte[8192];
                    while (!seen.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await socket.ReceiveAsync(buffer);
                        if (read == 0)
                            break;
                        seen.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    HasRequest.TrySetResult();
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // Stopped.
            }
        });
    }

    public Uri BaseUri { get; }

    public TaskCompletionSource HasRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        _listener.Stop();
        lock (_held)
            foreach (var socket in _held)
                socket.Dispose();
    }
}
