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

    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>Reads the next request, or null when the peer closed the connection (or sent something unparsable).</summary>
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
            (body, tooLarge) = await ReadChunkedAsync(ct).ConfigureAwait(false);
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

    private async Task<(byte[] Body, bool TooLarge)> ReadChunkedAsync(CancellationToken ct)
    {
        using var body = new MemoryStream();
        var tooLarge = false;
        while (true)
        {
            var sizeLine = await ReadLineAsync(ct).ConfigureAwait(false);
            if (sizeLine is null)
                return (tooLarge ? [] : body.ToArray(), tooLarge);
            var semicolon = sizeLine.IndexOf(';');
            var hex = (semicolon < 0 ? sizeLine : sizeLine[..semicolon]).Trim();
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size))
                return (tooLarge ? [] : body.ToArray(), tooLarge);
            if (size == 0)
            {
                await ReadLineAsync(ct).ConfigureAwait(false); // trailer terminator
                break;
            }

            if (tooLarge || body.Length + size > maxBodyBytes)
            {
                // Keep draining so the connection stays usable, but stop buffering.
                tooLarge = true;
                await DiscardAsync(size, ct).ConfigureAwait(false);
            }
            else
            {
                var chunk = await ReadExactAsync(size, ct).ConfigureAwait(false);
                body.Write(chunk, 0, chunk.Length);
            }

            await ReadLineAsync(ct).ConfigureAwait(false); // trailing CRLF
        }

        return (tooLarge ? [] : body.ToArray(), tooLarge);
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

    private async Task<string?> ReadLineAsync(CancellationToken ct)
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
            if (line.Count > 1024)
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
