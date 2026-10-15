using System.Globalization;
using System.Text;

namespace Kronikol.Extensions.Otlp;

/// <summary>One request read off a connection by <see cref="HttpRequestReader"/>.</summary>
/// <param name="Method">The HTTP method, verbatim.</param>
/// <param name="Target">The request target (path + query), verbatim.</param>
/// <param name="Path">The path part of <paramref name="Target"/>.</param>
/// <param name="Headers">Header names (case-insensitive) to values; repeated headers are joined with <c>", "</c>.</param>
/// <param name="Body">The decoded message body (de-chunked, still content-encoded).</param>
/// <param name="KeepAlive">Whether the connection may be reused.</param>
/// <param name="TooLarge">The body exceeded the configured cap and was not read.</param>
internal sealed record HttpRequestMessageData(
    string Method,
    string Target,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body,
    bool KeepAlive,
    bool TooLarge)
{
    public string? ContentType => Headers.TryGetValue("Content-Type", out var value) ? value : null;

    public string? ContentEncoding => Headers.TryGetValue("Content-Encoding", out var value) ? value : null;
}

/// <summary>How a request's body is framed, read from its head (RFC 9112 section 6.3).</summary>
internal enum BodyFraming
{
    /// <summary>Neither <c>Transfer-Encoding: chunked</c> nor <c>Content-Length</c>: no body.</summary>
    None,

    /// <summary>A <c>Content-Length</c> of digits within <see cref="int"/> range.</summary>
    ContentLength,

    /// <summary><c>Transfer-Encoding: chunked</c>, with or without a <c>Content-Length</c> beside it.</summary>
    Chunked,

    /// <summary>A <c>Content-Length</c> that is not a run of digits, or a list of values: the framing is lost (400).</summary>
    InvalidLength,

    /// <summary>A <c>Content-Length</c> of digits past <see cref="int.MaxValue"/>, over any cap (413).</summary>
    LengthPastInt,
}

/// <summary>
/// A request's head, read before its body (<see cref="HttpRequestReader.ReadHeadAsync"/>), so the tap can refuse it before
/// it allocates or reads anything of the body.
/// </summary>
/// <param name="Method">The HTTP method, verbatim.</param>
/// <param name="Target">The request target (path + query), verbatim.</param>
/// <param name="Path">The path part of <paramref name="Target"/>.</param>
/// <param name="Headers">Header names (case-insensitive) to values; repeated headers are joined with <c>", "</c>.</param>
/// <param name="KeepAlive">Whether the connection may be reused.</param>
/// <param name="Framing">How the body is framed.</param>
/// <param name="ContentLength">The declared length, for <see cref="BodyFraming.ContentLength"/>; otherwise zero.</param>
internal sealed record HttpRequestHead(
    string Method,
    string Target,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    bool KeepAlive,
    BodyFraming Framing,
    int ContentLength);

/// <summary>
/// A minimal HTTP/1.1 request reader over a raw socket stream — enough for one endpoint that receives
/// <c>POST</c>s with a <c>Content-Length</c> or chunked body, with keep-alive. Deliberately small: the tap
/// cannot use <c>HttpListener</c> because http.sys refuses non-loopback prefixes without a URL ACL, and a
/// full server would be a dependency for a single route.
/// </summary>
internal sealed class HttpRequestReader(Stream stream, int maxBodyBytes)
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxChunkLineBytes = 1024;
    // A body's buffer starts at this, or at its declared length when that is less, and doubles as the body arrives.
    private const int FirstBodyBytes = 64 * 1024;

    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;
    private int _bodyBytesHeld;

    // The size of the buffer held for the body being read (Kronikol.Tests.Otlp): a fact checks a declared length is not
    // allocated before its bytes arrive.
    internal int BodyBytesHeld => Volatile.Read(ref _bodyBytesHeld);

    /// <summary>Called after each read that moves bytes: the tap's stall limit starts its wait again.</summary>
    internal Action? Progressed { get; set; }

    /// <summary>
    /// Called once the next request's first byte is in hand, read or already buffered: the wait before it is a kept-alive
    /// connection's, the wait after it a stall.
    /// </summary>
    internal Action? HeadStarted { get; set; }

    /// <summary>Reads the next request whole, or null when the peer closed the connection, cut a body short, or sent something unparsable or refused.</summary>
    public async Task<HttpRequestMessageData?> ReadAsync(CancellationToken ct)
    {
        var head = await ReadHeadAsync(ct).ConfigureAwait(false);
        return head is null or { Framing: BodyFraming.InvalidLength or BodyFraming.LengthPastInt }
            ? null
            : await ReadBodyAsync(head, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the next request's head, or null when the peer closed the connection or sent something unparsable.</summary>
    public async Task<HttpRequestHead?> ReadHeadAsync(CancellationToken ct)
    {
        var head = await ReadHeadTextAsync(ct).ConfigureAwait(false);
        if (head is null)
            return null;

        var lines = head.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var method = requestLine[0];
        var target = requestLine[1];
        var version = requestLine.Length > 2 ? requestLine[2] : "HTTP/1.1";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = headers.TryGetValue(name, out var existing) ? existing + ", " + value : value;
        }

        var keepAlive = !version.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase);
        if (headers.TryGetValue("Connection", out var connection))
        {
            if (connection.Contains("close", StringComparison.OrdinalIgnoreCase))
                keepAlive = false;
            else if (connection.Contains("keep-alive", StringComparison.OrdinalIgnoreCase))
                keepAlive = true;
        }

        var framing = BodyFraming.None;
        var length = 0;
        var hasLength = headers.TryGetValue("Content-Length", out var lengthText);
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            framing = BodyFraming.Chunked;
            // Both at once: read by the Transfer-Encoding alone, and the connection closed after the answer, which RFC
            // 9112 section 6.1 says a server MUST do, since a peer in between may have read the body by the other one.
            if (hasLength)
                keepAlive = false;
        }
        else if (hasLength)
        {
            framing = ParseContentLength(lengthText!, out length);
        }

        return new HttpRequestHead(method, target, PathOf(target), headers, keepAlive, framing, length);
    }

    /// <summary>
    /// Reads the body <paramref name="head"/> frames, or null when it is cut short or malformed. A body over the cap is
    /// drained rather than buffered and comes back <see cref="HttpRequestMessageData.TooLarge"/>.
    /// </summary>
    public async Task<HttpRequestMessageData?> ReadBodyAsync(HttpRequestHead head, CancellationToken ct)
    {
        var keepAlive = head.KeepAlive;
        var tooLarge = false;
        byte[] body = [];
        switch (head.Framing)
        {
            case BodyFraming.Chunked:
                if (await ReadChunkedAsync(ct).ConfigureAwait(false) is not { } chunked)
                    return null; // cut short or malformed, as a Content-Length body cut short below
                (body, tooLarge) = (chunked.Body, chunked.TooLarge);
                keepAlive &= chunked.Reusable;
                break;
            case BodyFraming.ContentLength when head.ContentLength > maxBodyBytes:
                // Drain rather than buffer: the connection stays clean, so the 413 actually reaches the caller. A drain cut
                // short ends as a body cut short does: nothing to answer and nothing to count.
                if (!await DiscardAsync(head.ContentLength, ct).ConfigureAwait(false))
                    return null;
                tooLarge = true;
                break;
            case BodyFraming.ContentLength when head.ContentLength > 0:
                body = await ReadExactAsync(head.ContentLength, ct).ConfigureAwait(false);
                if (body.Length < head.ContentLength)
                    return null; // truncated
                break;
            case BodyFraming.InvalidLength or BodyFraming.LengthPastInt:
                throw new InvalidOperationException($"A request framed {head.Framing} has no body to read; it is refused first.");
        }

        return new HttpRequestMessageData(head.Method, head.Target, head.Path, head.Headers, body, keepAlive, tooLarge);
    }

    /// <summary>
    /// A <c>Content-Length</c> as RFC 9110 section 8.6 has it: digits alone, so a sign (<c>+5</c>, <c>-5</c>), a list
    /// (<c>10, 10</c>, or the header twice) and anything else is invalid framing, and digits past <see cref="int"/> are
    /// over any cap.
    /// </summary>
    private static BodyFraming ParseContentLength(string text, out int length)
    {
        length = 0;
        if (text.Length == 0)
            return BodyFraming.InvalidLength;
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
                return BodyFraming.InvalidLength;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out length)
            ? BodyFraming.ContentLength
            : BodyFraming.LengthPastInt;
    }

    /// <summary>
    /// Reads and throws away what the peer still sends, so that closing does not turn bytes it left unread into a reset,
    /// which loses an answer the peer has not read yet: until the end of the stream, or until <paramref name="declared"/>
    /// more bytes have come (counting what is already buffered), giving up after <paramref name="quiet"/> without a byte
    /// or <paramref name="total"/> in all (nginx's <c>lingering_timeout</c> and <c>lingering_time</c>). The bounds are on
    /// the real clock.
    /// </summary>
    public async Task LingerAsync(long? declared, TimeSpan quiet, TimeSpan total, CancellationToken ct)
    {
        var remaining = declared ?? long.MaxValue;
        var buffered = Math.Min(_end - _start, remaining);
        _start += (int)buffered;
        remaining -= buffered;

        using var all = CancellationTokenSource.CreateLinkedTokenSource(ct);
        all.CancelAfter(total);
        while (remaining > 0)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(all.Token);
            idle.CancelAfter(quiet);
            int read;
            try
            {
                read = await stream.ReadAsync(_buffer.AsMemory(), idle.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                return;
            }

            if (read == 0)
                return;
            remaining -= read;
        }
    }

    private static string PathOf(string target)
    {
        var question = target.IndexOf('?');
        var path = question < 0 ? target : target[..question];
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var slash = path.IndexOf('/', path.IndexOf("//", StringComparison.Ordinal) + 2);
            path = slash < 0 ? "/" : path[slash..];
        }

        return path.Length == 0 ? "/" : path;
    }

    private async Task<string?> ReadHeadTextAsync(CancellationToken ct)
    {
        var head = new List<byte>(1024);
        var matched = 0;
        var started = false;
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return null;
            if (!started)
            {
                started = true;
                HeadStarted?.Invoke();
            }

            while (_start < _end)
            {
                var b = _buffer[_start++];
                head.Add(b);
                matched = b switch
                {
                    (byte)'\r' when matched is 0 or 2 => matched + 1,
                    (byte)'\n' when matched is 1 or 3 => matched + 1,
                    _ => 0,
                };

                if (matched == 4)
                    return Encoding.UTF8.GetString(head.ToArray(), 0, head.Count - 4);
                if (head.Count > MaxHeaderBytes)
                    return null;
            }
        }
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        // Grown as the bytes arrive, never made at the declared length up front: a head may declare MaxRequestBytes and
        // then send nothing more.
        var result = new byte[Math.Min(count, FirstBodyBytes)];
        Volatile.Write(ref _bodyBytesHeld, result.Length);
        var written = 0;
        while (written < count)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                break;
            var available = Math.Min(_end - _start, count - written);
            if (written + available > result.Length)
            {
                Array.Resize(ref result, (int)Math.Min(count, Math.Max(2L * result.Length, written + available)));
                Volatile.Write(ref _bodyBytesHeld, result.Length);
            }

            Array.Copy(_buffer, _start, result, written, available);
            _start += available;
            written += available;
        }

        return written == count ? result : result[..written];
    }

    /// <summary>
    /// Reads a chunked body through its last chunk and trailer section. Null when the body is cut short or malformed: the
    /// stream ends first, a size line is not hex or runs past 1,024 bytes, a chunk is shorter than its size or not followed
    /// by its CRLF, or the trailers run past <see cref="MaxHeaderBytes"/>. A body over the cap is drained, so the connection
    /// stays usable; a chunk size past <see cref="int.MaxValue"/> is too large to drain, so the connection is not reused.
    /// </summary>
    private async Task<(byte[] Body, bool TooLarge, bool Reusable)?> ReadChunkedAsync(CancellationToken ct)
    {
        using var body = new MemoryStream();
        var tooLarge = false;
        while (true)
        {
            var sizeLine = await ReadLineAsync(MaxChunkLineBytes, ct).ConfigureAwait(false);
            if (sizeLine is null)
                return null;
            var semicolon = sizeLine.IndexOf(';');
            if (!TryParseChunkSize((semicolon < 0 ? sizeLine : sizeLine[..semicolon]).Trim(), out var size))
                return null;
            if (size > int.MaxValue)
                return ([], true, false);
            if (size == 0)
                break;

            if (tooLarge || body.Length + size > maxBodyBytes)
            {
                // Keep draining so the connection stays usable, but stop buffering.
                tooLarge = true;
                if (!await DiscardAsync((int)size, ct).ConfigureAwait(false))
                    return null;
            }
            else
            {
                var chunk = await ReadExactAsync((int)size, ct).ConfigureAwait(false);
                body.Write(chunk, 0, chunk.Length);
            }

            // A chunk's data ends with a CRLF. A chunk or a drain cut short ends with the stream, before it.
            if (await ReadLineAsync(MaxChunkLineBytes, ct).ConfigureAwait(false) is not "")
                return null;
        }

        // The trailer section: field lines up to an empty one (RFC 9112 section 7.1.2), so the next request on the
        // connection starts where it should. The fields are not kept. A field line, like a header line, is held only
        // to the section's cap.
        var trailerBytes = 0;
        while (true)
        {
            var trailer = await ReadLineAsync(MaxHeaderBytes, ct).ConfigureAwait(false);
            if (trailer is null)
                return null;
            if (trailer.Length == 0)
                break;
            trailerBytes += trailer.Length + 2;
            if (trailerBytes > MaxHeaderBytes)
                return null;
        }

        return (tooLarge ? [] : body.ToArray(), tooLarge, true);
    }

    /// <summary>
    /// A chunk size's hex digits as a number. A value past <see cref="int.MaxValue"/> stays past it rather than wrapping,
    /// as an <c>int</c> parse would (<c>80000000</c> is negative there), so the caller can call it too large.
    /// </summary>
    private static bool TryParseChunkSize(string hex, out long size)
    {
        size = 0;
        if (hex.Length == 0)
            return false;
        foreach (var c in hex)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;
            if (size <= int.MaxValue)
                size = size * 16 + digit;
        }

        return true;
    }

    /// <summary>Reads and throws away <paramref name="count"/> bytes; false when the stream ends first.</summary>
    private async Task<bool> DiscardAsync(int count, CancellationToken ct)
    {
        var remaining = count;
        while (remaining > 0)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return false;
            var available = Math.Min(_end - _start, remaining);
            _start += available;
            remaining -= available;
        }

        return true;
    }

    /// <summary>The next line without its line break, or null when it runs past <paramref name="maxBytes"/> or the stream ends before it starts.</summary>
    private async Task<string?> ReadLineAsync(int maxBytes, CancellationToken ct)
    {
        var line = new List<byte>(16);
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return line.Count == 0 ? null : Encoding.ASCII.GetString(line.ToArray());
            var b = _buffer[_start++];
            if (b == (byte)'\n')
                return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
            line.Add(b);
            if (line.Count > maxBytes)
                return null;
        }
    }

    private async Task<bool> FillAsync(CancellationToken ct)
    {
        _start = 0;
        _end = await stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
        if (_end == 0)
            return false;
        Progressed?.Invoke();
        return true;
    }
}
