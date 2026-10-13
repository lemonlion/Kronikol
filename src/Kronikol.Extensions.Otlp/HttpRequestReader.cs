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

    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>Reads the next request, or null when the peer closed the connection, cut a body short or sent something unparsable.</summary>
    public async Task<HttpRequestMessageData?> ReadAsync(CancellationToken ct)
    {
        var head = await ReadHeadAsync(ct).ConfigureAwait(false);
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

        var tooLarge = false;
        byte[] body = [];
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            if (await ReadChunkedAsync(ct).ConfigureAwait(false) is not { } chunked)
                return null; // cut short or malformed, as a Content-Length body cut short below
            (body, tooLarge) = (chunked.Body, chunked.TooLarge);
            keepAlive &= chunked.Reusable;
        }
        else if (headers.TryGetValue("Content-Length", out var lengthText)
                 && int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
                 && length > 0)
        {
            if (length > maxBodyBytes)
            {
                // Drain rather than buffer: the connection stays clean, so the 413 actually reaches the caller.
                await DiscardAsync(length, ct).ConfigureAwait(false);
                return new HttpRequestMessageData(method, target, PathOf(target), headers, [], keepAlive, true);
            }

            body = await ReadExactAsync(length, ct).ConfigureAwait(false);
            if (body.Length < length)
                return null; // truncated
        }

        return new HttpRequestMessageData(method, target, PathOf(target), headers, body, keepAlive, tooLarge);
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

    private async Task<string?> ReadHeadAsync(CancellationToken ct)
    {
        var head = new List<byte>(1024);
        var matched = 0;
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return null;

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
        var result = new byte[count];
        var written = 0;
        while (written < count)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                break;
            var available = Math.Min(_end - _start, count - written);
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
                await DiscardAsync((int)size, ct).ConfigureAwait(false);
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

    private async Task DiscardAsync(int count, CancellationToken ct)
    {
        var remaining = count;
        while (remaining > 0)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                return;
            var available = Math.Min(_end - _start, remaining);
            _start += available;
            remaining -= available;
        }
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
        return _end > 0;
    }
}
