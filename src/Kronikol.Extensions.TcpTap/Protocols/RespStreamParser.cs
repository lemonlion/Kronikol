using System.Globalization;
using System.Text;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// A resumable, streaming RESP2/RESP3 parser: feed it the segments of one direction of a tapped connection as
/// they arrive, in any segmentation, and it hands back every complete top-level value — without ever holding
/// more than one header line, one sub-cap bulk payload or one preview in memory.
/// </summary>
/// <remarks>
/// <para>RESP is length-prefixed everywhere, so the parser is a small state machine over a frame stack: a
/// header line is accumulated until CRLF; <c>$</c>/<c>=</c>/<c>!</c> payloads up to <c>maxBulkBytes</c> are
/// read in full; longer ones are <em>streamed past</em> — the first <c>previewBytes</c> are kept, the rest are
/// counted (<see cref="BytesSkipped"/>, <see cref="OversizePayloadsSkipped"/>) — and surface as a
/// <see cref="RespValue"/> with <see cref="RespValue.Truncated"/> set and <see cref="RespValue.DeclaredLength"/>
/// the length on the wire; aggregates push a frame and pop when their last element completes. Memory per
/// connection is therefore O(cap), never O(largest value), and no value is ever lost to its size.</para>
/// <para>Purely a reader of a copy of the bytes — it never sees, holds or forwards the real stream. Bytes held
/// for an unfinished top-level value (a header line, sub-cap payloads, the elements of an open aggregate) are
/// bounded by <c>maxHeldBytes</c>; crossing it means the stream is desynchronised (an aggregate count or bulk
/// length that was really payload) and raises a <em>recoverable</em> <see cref="TapProtocolException"/>. Bytes
/// that are not RESP at all raise <see cref="RespProtocolException"/>. <see cref="Reset"/> drops any partial
/// value so decoding can re-arm at a message boundary.</para>
/// </remarks>
public sealed class RespStreamParser
{
    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';

    /// <summary>A length-prefixed header (<c>$123</c>, <c>*5</c>, <c>:42</c>) longer than this without a CRLF is not RESP.</summary>
    private const int MaxHeaderBytes = 64;

    /// <summary>Aggregates nested deeper than this are not something Redis produces; treated as a protocol error.</summary>
    private const int MaxDepth = 256;

    /// <summary>
    /// The largest bulk length the parser believes (Redis's default <c>proto-max-bulk-len</c>). A bigger number is a
    /// payload byte misread as a header on a desynchronised stream — streaming "past" it would stall decoding for gigabytes.
    /// </summary>
    public const long MaxPlausibleBulkBytes = 512L * 1024 * 1024;

    private enum State
    {
        Line,
        Bulk,
        Skip,
        Trailer,
    }

    private readonly int _maxBulkBytes;
    private readonly int _previewBytes;
    private readonly long _maxHeldBytes;
    private readonly bool _acceptInlineCommands;
    private readonly Action<long, int>? _onOversizePayload;
    private readonly Stack<Frame> _frames = new();

    private State _state;
    private byte[] _line = new byte[128];
    private int _lineLength;
    private long _heldBytes;

    private RespType _bulkType;
    private byte[]? _bulk;
    private int _bulkFilled;

    private long _declaredLength;
    private long _remaining;
    private byte[]? _preview;
    private int _previewFilled;

    private RespValue? _pendingValue;
    private int _trailerRemaining;

    /// <summary>Creates a parser.</summary>
    /// <param name="maxBulkBytes">Bulk payloads longer than this are streamed past instead of buffered.</param>
    /// <param name="previewBytes">How many leading bytes of a streamed-past payload are kept as its preview (clamped to <paramref name="maxBulkBytes"/>).</param>
    /// <param name="maxHeldBytes">Bytes the parser may hold for one unfinished top-level value (header line, sub-cap payloads, open aggregates — not bytes being streamed past). Crossing it is a recoverable protocol error: the stream is desynchronised. Raised to fit one sub-cap bulk when smaller than <paramref name="maxBulkBytes"/>.</param>
    /// <param name="acceptInlineCommands">Whether a top-level line that does not start with <c>*</c> or <c>$</c> is a legacy inline command (<c>PING\r\n</c>) rather than a protocol error — true for the client→server direction.</param>
    /// <param name="onOversizePayload">Called once per streamed-past payload, after its last byte, with the declared length and the bytes kept.</param>
    public RespStreamParser(
        int maxBulkBytes, int previewBytes, long maxHeldBytes = 8 * 1024 * 1024,
        bool acceptInlineCommands = false, Action<long, int>? onOversizePayload = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBulkBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(previewBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeldBytes);
        _maxBulkBytes = maxBulkBytes;
        _previewBytes = Math.Min(previewBytes, maxBulkBytes);
        // A sub-cap bulk (header + payload + CRLF) must always fit, whatever the caller passed.
        _maxHeldBytes = Math.Max(maxHeldBytes, (long)maxBulkBytes + MaxHeaderBytes + 2);
        _acceptInlineCommands = acceptInlineCommands;
        _onOversizePayload = onOversizePayload;
    }

    /// <summary>Bytes of oversize payloads consumed without being kept (the preview is not counted).</summary>
    public long BytesSkipped { get; private set; }

    /// <summary>How many bulk payloads were longer than the cap and were streamed past.</summary>
    public int OversizePayloadsSkipped { get; private set; }

    /// <summary>The declared length of the largest payload streamed past so far.</summary>
    public long LargestOversizePayload { get; private set; }

    /// <summary>Top-level values completed so far (across resets).</summary>
    public long ValuesCompleted { get; private set; }

    /// <summary>Inline command lines (<c>PING\r\n</c>) seen so far — non-zero means the client is something like <c>redis-cli</c>, not a RESP-array client library.</summary>
    public long InlineCommands { get; private set; }

    /// <summary>True between values: nothing partial is held, no aggregate is open.</summary>
    public bool IsAtValueBoundary => _state == State.Line && _lineLength == 0 && _frames.Count == 0;

    /// <summary>Whether the parser is currently streaming past an oversize payload.</summary>
    public bool IsStreamingPast => _state == State.Skip;

    /// <summary>How deep inside aggregates the parser currently is (0 between top-level values).</summary>
    public int Depth => _frames.Count;

    /// <summary>Bytes held for the unfinished top-level value (0 at a boundary).</summary>
    public long HeldBytes => _heldBytes;

    /// <summary>
    /// Consumes a segment, invoking <paramref name="onValue"/> for every top-level value that completes inside it.
    /// The segment may start or end anywhere — mid-line, mid-payload, between the CR and the LF.
    /// </summary>
    /// <exception cref="RespProtocolException">The bytes are not RESP (unknown marker, malformed or implausible length, a bare LF, implausible nesting).</exception>
    /// <exception cref="TapProtocolException">Recoverable: more than the held-bytes cap accumulated without completing a value — a desynchronised stream.</exception>
    public void Feed(ReadOnlySpan<byte> data, Action<RespValue> onValue)
    {
        ArgumentNullException.ThrowIfNull(onValue);
        var offset = 0;
        while (offset < data.Length)
        {
            switch (_state)
            {
                case State.Line:
                    offset = ReadLine(data, offset, onValue);
                    break;

                case State.Bulk:
                {
                    var take = Math.Min(_bulk!.Length - _bulkFilled, data.Length - offset);
                    data.Slice(offset, take).CopyTo(_bulk.AsSpan(_bulkFilled));
                    _bulkFilled += take;
                    offset += take;
                    Hold(take);
                    if (_bulkFilled == _bulk.Length)
                    {
                        _pendingValue = new RespValue { Type = _bulkType, Bytes = _bulk, DeclaredLength = _bulk.Length };
                        _bulk = null;
                        _state = State.Trailer;
                        _trailerRemaining = 2;
                    }

                    break;
                }

                case State.Skip:
                {
                    var take = (int)Math.Min(_remaining, data.Length - offset);
                    var kept = 0;
                    if (_previewFilled < _preview!.Length)
                    {
                        kept = Math.Min(_preview.Length - _previewFilled, take);
                        data.Slice(offset, kept).CopyTo(_preview.AsSpan(_previewFilled));
                        _previewFilled += kept;
                    }

                    BytesSkipped += take - kept;
                    _remaining -= take;
                    offset += take;
                    if (_remaining == 0)
                    {
                        OversizePayloadsSkipped++;
                        LargestOversizePayload = Math.Max(LargestOversizePayload, _declaredLength);
                        var preview = _preview;
                        _preview = null;
                        _pendingValue = new RespValue
                        {
                            Type = _bulkType, Bytes = preview, Truncated = true, DeclaredLength = _declaredLength,
                        };
                        _state = State.Trailer;
                        _trailerRemaining = 2;
                        _onOversizePayload?.Invoke(_declaredLength, preview.Length);
                    }

                    break;
                }

                case State.Trailer:
                {
                    var take = Math.Min(_trailerRemaining, data.Length - offset);
                    offset += take;
                    _trailerRemaining -= take;
                    Hold(take);
                    if (_trailerRemaining == 0)
                    {
                        var value = _pendingValue!;
                        _pendingValue = null;
                        _state = State.Line;
                        Complete(value, onValue);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>Drops any partial value and open aggregates, so the next byte fed is read as the start of a value. Counters are kept.</summary>
    public void Reset()
    {
        _frames.Clear();
        _state = State.Line;
        _lineLength = 0;
        _heldBytes = 0;
        _bulk = null;
        _bulkFilled = 0;
        _preview = null;
        _previewFilled = 0;
        _remaining = 0;
        _declaredLength = 0;
        _pendingValue = null;
        _trailerRemaining = 0;
    }

    private void Hold(int count)
    {
        _heldBytes += count;
        if (_heldBytes > _maxHeldBytes)
            throw new TapProtocolException(
                $"Held {_heldBytes} bytes without completing a value, over the {_maxHeldBytes}-byte cap (TcpTapOptions.MaxBufferedBytes) — the stream is desynchronised.",
                recoverable: true);
    }

    private int ReadLine(ReadOnlySpan<byte> data, int offset, Action<RespValue> onValue)
    {
        var span = data[offset..];
        var lf = span.IndexOf(Lf);
        var chunk = lf < 0 ? span : span[..(lf + 1)];

        Hold(chunk.Length);

        if (_line.Length - _lineLength < chunk.Length)
        {
            var size = (long)_line.Length;
            while (size - _lineLength < chunk.Length)
                size *= 2;
            Array.Resize(ref _line, (int)Math.Min(size, Math.Min(_maxHeldBytes + 2, int.MaxValue - 64)));
        }

        chunk.CopyTo(_line.AsSpan(_lineLength));
        _lineLength += chunk.Length;

        if (lf < 0)
        {
            if (_lineLength > MaxHeaderBytes && IsLengthPrefixedHeader(_line[0]) && !InlineEligible(_line[0]))
                throw new RespProtocolException($"RESP header '{(char)_line[0]}…' has no line end within {MaxHeaderBytes} bytes.");
            return data.Length;
        }

        if (_lineLength < 2 || _line[_lineLength - 2] != Cr)
            throw new RespProtocolException("RESP line ended with a bare LF (no CR).");

        var line = _line.AsSpan(0, _lineLength - 2);
        _lineLength = 0;
        ParseLine(line, onValue);
        return offset + chunk.Length;
    }

    private bool InlineEligible(byte first) =>
        _acceptInlineCommands && _frames.Count == 0 && first is not ((byte)'*' or (byte)'$');

    private static bool IsLengthPrefixedHeader(byte marker) =>
        marker is (byte)'$' or (byte)'=' or (byte)'!' or (byte)'*' or (byte)'~' or (byte)'>' or (byte)'%' or (byte)':';

    private void ParseLine(ReadOnlySpan<byte> line, Action<RespValue> onValue)
    {
        if (_acceptInlineCommands && _frames.Count == 0 && (line.Length == 0 || InlineEligible(line[0])))
        {
            // Inline command (redis-cli, health probes): "PING\r\n". An empty line is nothing.
            var text = Encoding.UTF8.GetString(line);
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                _heldBytes = 0;
                return;
            }

            var items = new List<RespValue>(parts.Length);
            foreach (var part in parts)
                items.Add(new RespValue { Type = RespType.BulkString, Text = part });
            InlineCommands++;
            Complete(new RespValue { Type = RespType.Array, Items = items }, onValue);
            return;
        }

        if (line.Length == 0)
            throw new RespProtocolException("Empty RESP line.");

        var marker = line[0];
        var body = line[1..];
        switch (marker)
        {
            case (byte)'+':
                Complete(new RespValue { Type = RespType.SimpleString, Text = Encoding.UTF8.GetString(body) }, onValue);
                return;
            case (byte)'-':
                Complete(new RespValue { Type = RespType.Error, Text = Encoding.UTF8.GetString(body) }, onValue);
                return;
            case (byte)':':
            {
                var text = Encoding.UTF8.GetString(body);
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    throw new RespProtocolException($"Malformed RESP integer ':{text}'.");
                Complete(new RespValue { Type = RespType.Integer, Integer = number, Text = text }, onValue);
                return;
            }
            case (byte)',':
                Complete(new RespValue { Type = RespType.Double, Text = Encoding.UTF8.GetString(body) }, onValue);
                return;
            case (byte)'(':
                Complete(new RespValue { Type = RespType.BigNumber, Text = Encoding.UTF8.GetString(body) }, onValue);
                return;
            case (byte)'#':
                Complete(new RespValue { Type = RespType.Boolean, Text = body.Length == 1 && body[0] == (byte)'t' ? "true" : "false" }, onValue);
                return;
            case (byte)'_':
                Complete(new RespValue { Type = RespType.Null }, onValue);
                return;
            case (byte)'$':
                BeginBulk(RespType.BulkString, body, onValue);
                return;
            case (byte)'=':
                BeginBulk(RespType.VerbatimString, body, onValue);
                return;
            case (byte)'!':
                BeginBulk(RespType.BlobError, body, onValue);
                return;
            case (byte)'*':
                BeginAggregate(RespType.Array, 1, body, onValue);
                return;
            case (byte)'~':
                BeginAggregate(RespType.Set, 1, body, onValue);
                return;
            case (byte)'>':
                BeginAggregate(RespType.Push, 1, body, onValue);
                return;
            case (byte)'%':
                BeginAggregate(RespType.Map, 2, body, onValue);
                return;
            default:
                throw new RespProtocolException($"Unexpected RESP type marker 0x{marker:x2} ('{(char)marker}').");
        }
    }

    private void BeginBulk(RespType type, ReadOnlySpan<byte> lengthBytes, Action<RespValue> onValue)
    {
        var lengthText = Encoding.UTF8.GetString(lengthBytes);
        if (!long.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            throw new RespProtocolException($"Malformed RESP bulk length '{lengthText}'.");

        if (length < 0)
        {
            Complete(new RespValue { Type = RespType.Null }, onValue);
            return;
        }

        if (length > MaxPlausibleBulkBytes)
            throw new RespProtocolException($"Implausible RESP bulk length {lengthText} (over {MaxPlausibleBulkBytes} bytes) — payload bytes read as a header.");

        _bulkType = type;
        if (length <= _maxBulkBytes)
        {
            if (length == 0)
            {
                _pendingValue = new RespValue { Type = type, Bytes = [], DeclaredLength = 0 };
                _state = State.Trailer;
                _trailerRemaining = 2;
                return;
            }

            _bulk = new byte[length];
            _bulkFilled = 0;
            _state = State.Bulk;
            return;
        }

        // Longer than the cap: stream it past, keeping a preview. The whole payload is never in memory.
        _declaredLength = length;
        _remaining = length;
        _preview = new byte[(int)Math.Min(_previewBytes, length)];
        _previewFilled = 0;
        _state = State.Skip;
    }

    private void BeginAggregate(RespType type, int itemsPerEntry, ReadOnlySpan<byte> countBytes, Action<RespValue> onValue)
    {
        var countText = Encoding.UTF8.GetString(countBytes);
        if (!long.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            throw new RespProtocolException($"Malformed RESP aggregate length '{countText}'.");

        if (count < 0)
        {
            Complete(new RespValue { Type = RespType.Null }, onValue);
            return;
        }

        var total = count * itemsPerEntry;
        if (total > int.MaxValue)
            throw new RespProtocolException($"Implausible RESP aggregate length {countText}.");

        if (total == 0)
        {
            Complete(new RespValue { Type = type, Items = [] }, onValue);
            return;
        }

        if (_frames.Count >= MaxDepth)
            throw new RespProtocolException($"RESP aggregates nested deeper than {MaxDepth} levels.");

        _frames.Push(new Frame(type, (int)total));
    }

    private void Complete(RespValue value, Action<RespValue> onValue)
    {
        while (true)
        {
            if (_frames.Count == 0)
            {
                _heldBytes = 0;
                ValuesCompleted++;
                onValue(value);
                return;
            }

            var frame = _frames.Peek();
            frame.Items.Add(value);
            if (frame.Items.Count < frame.Total)
                return;

            _frames.Pop();
            value = new RespValue { Type = frame.Type, Items = frame.Items };
        }
    }

    private sealed class Frame(RespType type, int total)
    {
        public RespType Type { get; } = type;

        public int Total { get; } = total;

        public List<RespValue> Items { get; } = new(Math.Min(total, 64));
    }
}
