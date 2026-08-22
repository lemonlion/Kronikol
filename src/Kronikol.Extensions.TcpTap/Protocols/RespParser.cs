using System.Globalization;
using System.Text;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>Raised when the bytes on the wire are not RESP at all; the tap stops decoding that connection.</summary>
public sealed class RespProtocolException(string message) : TapProtocolException(message);

/// <summary>
/// An incremental RESP2/RESP3 parser: feed it whatever arrived, take whatever complete values are in there.
/// </summary>
/// <remarks>
/// Purely a reader of a copy of the bytes — it never sees, holds or forwards the real stream. Both protocol
/// versions parse with the same code because every RESP value is self-describing; a connection that upgrades
/// with <c>HELLO 3</c> simply starts producing the RESP3 types.
/// </remarks>
public static class RespParser
{
    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';

    /// <summary>
    /// Tries to read one complete value from the front of <paramref name="buffer"/>.
    /// </summary>
    /// <returns>True when a whole value was available; false when more bytes are needed.</returns>
    /// <exception cref="RespProtocolException">The bytes are not valid RESP.</exception>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        if (buffer.Length == 0)
            return false;

        var marker = buffer[0];
        return marker switch
        {
            (byte)'+' => TryLine(buffer, RespType.SimpleString, out value, out consumed),
            (byte)'-' => TryLine(buffer, RespType.Error, out value, out consumed),
            (byte)':' => TryInteger(buffer, out value, out consumed),
            (byte)',' => TryLine(buffer, RespType.Double, out value, out consumed),
            (byte)'(' => TryLine(buffer, RespType.BigNumber, out value, out consumed),
            (byte)'#' => TryBoolean(buffer, out value, out consumed),
            (byte)'_' => TryNull(buffer, out value, out consumed),
            (byte)'$' => TryBulk(buffer, RespType.BulkString, out value, out consumed),
            (byte)'=' => TryBulk(buffer, RespType.VerbatimString, out value, out consumed),
            (byte)'!' => TryBulk(buffer, RespType.BlobError, out value, out consumed),
            (byte)'*' => TryAggregate(buffer, RespType.Array, 1, out value, out consumed),
            (byte)'~' => TryAggregate(buffer, RespType.Set, 1, out value, out consumed),
            (byte)'>' => TryAggregate(buffer, RespType.Push, 1, out value, out consumed),
            (byte)'%' => TryAggregate(buffer, RespType.Map, 2, out value, out consumed),
            _ => throw new RespProtocolException($"Unexpected RESP type marker 0x{marker:x2} ('{(char)marker}')."),
        };
    }

    /// <summary>
    /// Tries to read one client command: a RESP array of bulk strings, or a legacy inline command line.
    /// Returns the arguments as text (binary-unsafe arguments are decoded as UTF-8 with replacement).
    /// </summary>
    public static bool TryParseCommand(ReadOnlySpan<byte> buffer, out string[]? arguments, out int consumed)
    {
        arguments = null;
        consumed = 0;
        if (buffer.Length == 0)
            return false;

        if (buffer[0] is (byte)'*' or (byte)'$')
        {
            if (!TryParse(buffer, out var value, out consumed))
                return false;

            arguments = value!.Type switch
            {
                RespType.Array => (value.Items ?? []).Select(i => i.AsText() ?? string.Empty).ToArray(),
                RespType.Null => [],
                RespType.BulkString => [value.AsText() ?? string.Empty],
                _ => throw new RespProtocolException($"A client command must be an array of bulk strings, got {value.Type}."),
            };
            return true;
        }

        // Inline command (redis-cli, health probes): "PING\r\n".
        var end = IndexOfCrLf(buffer, 0);
        if (end < 0)
            return false;
        var line = Encoding.UTF8.GetString(buffer[..end]);
        consumed = end + 2;
        arguments = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return true;
    }

    private static bool TryLine(ReadOnlySpan<byte> buffer, RespType type, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var end = IndexOfCrLf(buffer, 1);
        if (end < 0)
            return false;
        value = new RespValue { Type = type, Text = Encoding.UTF8.GetString(buffer[1..end]) };
        consumed = end + 2;
        return true;
    }

    private static bool TryInteger(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var end = IndexOfCrLf(buffer, 1);
        if (end < 0)
            return false;
        var text = Encoding.UTF8.GetString(buffer[1..end]);
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new RespProtocolException($"Malformed RESP integer ':{text}'.");
        value = new RespValue { Type = RespType.Integer, Integer = number, Text = text };
        consumed = end + 2;
        return true;
    }

    private static bool TryBoolean(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var end = IndexOfCrLf(buffer, 1);
        if (end < 0)
            return false;
        var text = Encoding.UTF8.GetString(buffer[1..end]);
        value = new RespValue { Type = RespType.Boolean, Text = text == "t" ? "true" : "false" };
        consumed = end + 2;
        return true;
    }

    private static bool TryNull(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var end = IndexOfCrLf(buffer, 1);
        if (end < 0)
            return false;
        value = new RespValue { Type = RespType.Null };
        consumed = end + 2;
        return true;
    }

    private static bool TryBulk(ReadOnlySpan<byte> buffer, RespType type, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var headerEnd = IndexOfCrLf(buffer, 1);
        if (headerEnd < 0)
            return false;

        var lengthText = Encoding.UTF8.GetString(buffer[1..headerEnd]);
        if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            throw new RespProtocolException($"Malformed RESP bulk length '{lengthText}'.");

        if (length < 0)
        {
            value = new RespValue { Type = RespType.Null };
            consumed = headerEnd + 2;
            return true;
        }

        var payloadStart = headerEnd + 2;
        if (buffer.Length < payloadStart + length + 2)
            return false;

        var payload = buffer.Slice(payloadStart, length).ToArray();
        value = new RespValue { Type = type, Bytes = payload };
        consumed = payloadStart + length + 2;
        return true;
    }

    private static bool TryAggregate(ReadOnlySpan<byte> buffer, RespType type, int itemsPerEntry, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;
        var headerEnd = IndexOfCrLf(buffer, 1);
        if (headerEnd < 0)
            return false;

        var countText = Encoding.UTF8.GetString(buffer[1..headerEnd]);
        if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            throw new RespProtocolException($"Malformed RESP aggregate length '{countText}'.");

        if (count < 0)
        {
            value = new RespValue { Type = RespType.Null };
            consumed = headerEnd + 2;
            return true;
        }

        var offset = headerEnd + 2;
        var total = count * itemsPerEntry;
        var items = new List<RespValue>(total);
        for (var i = 0; i < total; i++)
        {
            if (!TryParse(buffer[offset..], out var item, out var itemConsumed))
                return false;
            items.Add(item!);
            offset += itemConsumed;
        }

        value = new RespValue { Type = type, Items = items };
        consumed = offset;
        return true;
    }

    private static int IndexOfCrLf(ReadOnlySpan<byte> buffer, int from)
    {
        for (var i = from; i + 1 < buffer.Length; i++)
        {
            if (buffer[i] == Cr && buffer[i + 1] == Lf)
                return i;
        }

        return -1;
    }
}
