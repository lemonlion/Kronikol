namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// A growable, compacting byte buffer for one direction of a tapped connection: append what arrived, parse
/// whole messages off the front, keep the remainder for the next segment.
/// </summary>
/// <remarks>Not thread-safe by design — a tap hands every segment of one connection to a single decoder task.</remarks>
internal sealed class WireBuffer(int maxBytes)
{
    private byte[] _buffer = new byte[4096];
    private int _start;
    private int _end;

    /// <summary>Bytes currently held.</summary>
    public int Length => _end - _start;

    /// <summary>The unparsed bytes.</summary>
    public ReadOnlySpan<byte> Span => _buffer.AsSpan(_start, _end - _start);

    /// <summary>Appends a segment, growing (never past the cap) and compacting as needed.</summary>
    /// <exception cref="TapProtocolException">The unparsed remainder exceeded the cap — a message far larger than any real one, or a desynchronised stream.</exception>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return;

        if (_end - _start + data.Length > maxBytes)
            throw new TapProtocolException(
                $"Buffered {_end - _start + data.Length} undecoded bytes, over the {maxBytes}-byte cap (TcpTapOptions.MaxBufferedBytes).");

        if (_buffer.Length - _end < data.Length)
        {
            Compact();
            if (_buffer.Length - _end < data.Length)
            {
                var size = _buffer.Length;
                while (size - (_end - _start) < data.Length)
                    size *= 2;
                var grown = new byte[Math.Min(size, Math.Max(maxBytes, data.Length + (_end - _start)))];
                Array.Copy(_buffer, _start, grown, 0, _end - _start);
                _end -= _start;
                _start = 0;
                _buffer = grown;
            }
        }

        data.CopyTo(_buffer.AsSpan(_end));
        _end += data.Length;
    }

    /// <summary>Discards <paramref name="count"/> bytes from the front.</summary>
    public void Consume(int count)
    {
        _start += count;
        if (_start == _end)
            _start = _end = 0;
        else if (_start > 65536)
            Compact();
    }

    /// <summary>Drops everything held.</summary>
    public void Clear() => _start = _end = 0;

    private void Compact()
    {
        if (_start == 0)
            return;
        Array.Copy(_buffer, _start, _buffer, 0, _end - _start);
        _end -= _start;
        _start = 0;
    }
}
