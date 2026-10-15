using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// An exporter on a raw socket, for what an HTTP client will not do on purpose: declare a body it never sends, send a
/// head no client would, or stop reading (plans/TAP_CALLER_TIME_LIMITS_PLAN.md).
/// </summary>
internal sealed class RawExporter : IDisposable
{
    private RawExporter(TcpClient client) => Client = client;

    public TcpClient Client { get; }

    public static async Task<RawExporter> ConnectAsync(OtlpTap tap, int? receiveBuffer = null)
    {
        var client = new TcpClient();
        if (receiveBuffer is { } size)
            client.ReceiveBufferSize = size; // small, so an answer it stops reading backs up into the tap's writes
        await client.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
        return new RawExporter(client);
    }

    public Task SendAsync(string text) => SendAsync(Encoding.ASCII.GetBytes(text));

    public async Task SendAsync(byte[] bytes) => await Client.Client.SendAsync(bytes);

    /// <summary>Everything the exporter hears until the tap lets it go (an end of stream or a reset), or until <paramref name="patience"/> runs out.</summary>
    public async Task<Answer> HearAsync(TimeSpan patience)
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
                    return new Answer([.. heard], "an end of stream");
                heard.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            return new Answer([.. heard], $"nothing more within {patience.TotalSeconds:0.#} s");
        }
        catch (SocketException ex)
        {
            return new Answer([.. heard], $"a reset ({ex.SocketErrorCode})");
        }
    }

    public void Dispose() => Client.Dispose();
}

/// <summary>What a <see cref="RawExporter"/> heard, and how its connection ended.</summary>
internal sealed record Answer(byte[] Bytes, string Ending)
{
    public string Text => Encoding.ASCII.GetString(Bytes);

    /// <summary>The first status line, or how the connection ended when the exporter heard nothing.</summary>
    public string FirstLine
    {
        get
        {
            var end = Text.IndexOf("\r\n", StringComparison.Ordinal);
            return Bytes.Length == 0 ? Ending : end < 0 ? Text : Text[..end];
        }
    }

    /// <summary>The status line of every answer heard, in order, each answer's body skipped by its <c>Content-Length</c>.</summary>
    public string[] StatusLines
    {
        get
        {
            var lines = new List<string>();
            var text = Text;
            var at = 0;
            while (at < text.Length)
            {
                var headEnd = text.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
                if (headEnd < 0)
                    break;
                var head = text[at..headEnd].Split("\r\n");
                lines.Add(head[0]);
                var length = head.Skip(1)
                    .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => int.Parse(l["Content-Length:".Length..].Trim()))
                    .FirstOrDefault();
                at = headEnd + 4 + length;
            }

            return [.. lines];
        }
    }

    /// <summary>Whether the connection ended with a FIN: an end of stream, neither a reset nor silence.</summary>
    public bool Closed => Ending == "an end of stream";

    public override string ToString() => $"{string.Join(" | ", StatusLines)} ({Bytes.Length:N0} bytes, then {Ending})";
}
