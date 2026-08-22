using System.Text;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>The RESP wire types, RESP2 and RESP3 (the RESP3 additions are marked).</summary>
public enum RespType
{
    /// <summary><c>+OK</c></summary>
    SimpleString,

    /// <summary><c>-ERR …</c></summary>
    Error,

    /// <summary><c>:1</c></summary>
    Integer,

    /// <summary><c>$3\r\nabc</c></summary>
    BulkString,

    /// <summary><c>*2\r\n…</c></summary>
    Array,

    /// <summary><c>$-1</c>, <c>*-1</c> (RESP2) or <c>_</c> (RESP3).</summary>
    Null,

    /// <summary>RESP3 <c>%n</c> — key/value pairs, flattened into <see cref="RespValue.Items"/>.</summary>
    Map,

    /// <summary>RESP3 <c>~n</c>.</summary>
    Set,

    /// <summary>RESP3 <c>&gt;n</c> — an out-of-band push, never a command reply.</summary>
    Push,

    /// <summary>RESP3 <c>,1.5</c>.</summary>
    Double,

    /// <summary>RESP3 <c>#t</c> / <c>#f</c>.</summary>
    Boolean,

    /// <summary>RESP3 <c>(1234…</c>.</summary>
    BigNumber,

    /// <summary>RESP3 <c>=15\r\ntxt:…</c>.</summary>
    VerbatimString,

    /// <summary>RESP3 <c>!21\r\nSYNTAX …</c>.</summary>
    BlobError,
}

/// <summary>One parsed RESP value. Bulk payloads keep their bytes; everything else keeps its text.</summary>
public sealed class RespValue
{
    /// <summary>The wire type.</summary>
    public RespType Type { get; init; }

    /// <summary>Line text for the textual types (simple string, error, double, boolean, big number, verbatim body).</summary>
    public string? Text { get; init; }

    /// <summary>Payload bytes for bulk strings, verbatim strings and blob errors.</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Value of an integer reply.</summary>
    public long? Integer { get; init; }

    /// <summary>Elements of an array/set/push, or the flattened key,value,… of a map.</summary>
    public IReadOnlyList<RespValue>? Items { get; init; }

    /// <summary>Whether this is a null bulk string, null array or RESP3 null.</summary>
    public bool IsNull => Type == RespType.Null;

    /// <summary>Whether this is an error reply (<c>-ERR …</c> or a RESP3 blob error).</summary>
    public bool IsError => Type is RespType.Error or RespType.BlobError;

    /// <summary>The value as text: bulk payloads decoded as UTF-8, integers formatted, aggregates left to <see cref="Render"/>.</summary>
    public string? AsText() => Type switch
    {
        RespType.Null => null,
        RespType.Integer => Integer?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        RespType.BulkString or RespType.VerbatimString or RespType.BlobError =>
            Bytes is null ? Text : Encoding.UTF8.GetString(Bytes),
        _ => Text,
    };

    /// <summary>A compact, human-readable rendering suitable for a diagram note.</summary>
    public string? Render()
    {
        switch (Type)
        {
            case RespType.Null:
                return null;
            case RespType.Array:
            case RespType.Set:
            case RespType.Push:
                return "[" + string.Join(", ", (Items ?? []).Select(i => i.Render() ?? "(nil)")) + "]";
            case RespType.Map:
            {
                var items = Items ?? [];
                var pairs = new List<string>(items.Count / 2);
                for (var i = 0; i + 1 < items.Count; i += 2)
                    pairs.Add($"{items[i].Render() ?? "(nil)"}={items[i + 1].Render() ?? "(nil)"}");
                return "{" + string.Join(", ", pairs) + "}";
            }
            default:
                return AsText();
        }
    }

    /// <summary>
    /// Whether the reply counts as a cache hit for the in-process extensions' hit/miss rule: a non-null value,
    /// or an aggregate with at least one non-null element.
    /// </summary>
    public bool HasResult() => Type switch
    {
        RespType.Null => false,
        RespType.Error or RespType.BlobError => false,
        RespType.Array or RespType.Set or RespType.Push => (Items ?? []).Any(i => !i.IsNull),
        RespType.Map => (Items ?? []).Count > 0,
        RespType.Boolean => Text == "true",
        RespType.BulkString or RespType.VerbatimString => true,
        _ => true,
    };
}
