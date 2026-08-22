using System.Text;
using Kronikol.Extensions.TcpTap.Protocols;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// The streaming parser must agree with the stateless <see cref="RespParser"/> on every value the latter can
/// read, whatever the segmentation — and keep going where the stateless one cannot: payloads larger than the cap.
/// </summary>
public class RespStreamParserTests
{
    private static List<RespValue> Feed(RespStreamParser parser, byte[] wire, IEnumerable<int> chunkSizes)
    {
        var values = new List<RespValue>();
        var offset = 0;
        foreach (var size in chunkSizes)
        {
            if (offset >= wire.Length)
                break;
            var take = Math.Min(size, wire.Length - offset);
            parser.Feed(wire.AsSpan(offset, take), values.Add);
            offset += take;
        }

        if (offset < wire.Length)
            parser.Feed(wire.AsSpan(offset), values.Add);
        return values;
    }

    private static List<RespValue> FeedByteByByte(RespStreamParser parser, string wire) =>
        Feed(parser, Encoding.UTF8.GetBytes(wire), Enumerable.Repeat(1, wire.Length));

    private static List<RespValue> FeedWhole(RespStreamParser parser, string wire) =>
        Feed(parser, Encoding.UTF8.GetBytes(wire), [int.MaxValue]);

    private static IEnumerable<int> RandomChunks(Random random, int total)
    {
        var remaining = total;
        while (remaining > 0)
        {
            var size = random.Next(1, Math.Min(remaining, 7) + 1);
            remaining -= size;
            yield return size;
        }
    }

    private static RespStreamParser Parser(int maxBulk = 1024, int preview = 1024, long maxHeld = 8 * 1024 * 1024, bool inline = false, Action<long, int>? onOversize = null) =>
        new(maxBulk, preview, maxHeld, inline, onOversize);

    /// <summary>The corpus <see cref="RespParserTests"/> pins, every RESP2 and RESP3 type, fed whole / byte-by-byte / randomly.</summary>
    private static readonly string[] Wires =
    [
        "+OK\r\n",
        "-WRONGTYPE Operation against a key holding the wrong kind of value\r\n",
        ":42\r\n",
        ":-7\r\n",
        "$5\r\nhello\r\n",
        "$0\r\n\r\n",
        "$5\r\na\r\nb!\r\n",
        "$-1\r\n",
        "*-1\r\n",
        "*3\r\n$1\r\na\r\n:2\r\n+three\r\n",
        "*0\r\n",
        "*2\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n*1\r\n:9\r\n",
        "*2\r\n$-1\r\n$1\r\nb\r\n",
        "_\r\n",
        ",1.23\r\n",
        "#t\r\n",
        "#f\r\n",
        "(3492890328409238509324850943850943825024385\r\n",
        "=15\r\ntxt:Some string\r\n",
        "!21\r\nSYNTAX invalid syntax\r\n",
        "%2\r\n$5\r\nfirst\r\n:1\r\n$6\r\nsecond\r\n:2\r\n",
        "~2\r\n$1\r\na\r\n$1\r\nb\r\n",
        ">3\r\n$7\r\nmessage\r\n$3\r\nfoo\r\n$3\r\nbar\r\n",
        "%1\r\n$6\r\nserver\r\n*2\r\n$5\r\nredis\r\n%1\r\n+k\r\n_\r\n",
    ];

    public static TheoryData<string> Corpus()
    {
        var data = new TheoryData<string>();
        foreach (var wire in Wires)
            data.Add(wire);
        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryValueAgreesWithTheStatelessParserWhateverTheSegmentation(string wire)
    {
        Assert.True(RespParser.TryParse(Encoding.UTF8.GetBytes(wire), out var expected, out _));

        foreach (var values in new[]
                 {
                     FeedWhole(Parser(), wire),
                     FeedByteByByte(Parser(), wire),
                     Feed(Parser(), Encoding.UTF8.GetBytes(wire), RandomChunks(new Random(wire.Length), wire.Length).ToList()),
                 })
        {
            var actual = Assert.Single(values);
            Assert.Equal(expected!.Type, actual.Type);
            Assert.Equal(expected.Render(), actual.Render());
            Assert.Equal(expected.HasResult(), actual.HasResult());
            Assert.Equal(expected.IsNull, actual.IsNull);
            Assert.Equal(expected.IsError, actual.IsError);
            Assert.Equal(expected.Integer, actual.Integer);
        }
    }

    [Fact]
    public void RandomSegmentationsOfAPipelineProduceTheSameValuesInOrder()
    {
        var wire = string.Concat(Wires);
        var bytes = Encoding.UTF8.GetBytes(wire);
        var expected = FeedWhole(Parser(), wire).Select(v => $"{v.Type}:{v.Render()}").ToArray();
        Assert.Equal(Wires.Length, expected.Length);

        var random = new Random(20260822);
        for (var round = 0; round < 200; round++)
        {
            var parser = Parser();
            var actual = Feed(parser, bytes, RandomChunks(random, bytes.Length).ToList()).Select(v => $"{v.Type}:{v.Render()}").ToArray();
            Assert.Equal(expected, actual);
            Assert.True(parser.IsAtValueBoundary);
        }
    }

    // ---- oversize payloads ------------------------------------------------------------------------

    [Fact]
    public void ABulkOverTheCapIsStreamedPastWithAPreviewAndItsDeclaredLength()
    {
        var payload = new string('x', 100);
        (long Declared, int Kept)? reported = null;
        var parser = Parser(maxBulk: 10, preview: 4, onOversize: (declared, kept) => reported = (declared, kept));

        var value = Assert.Single(FeedByteByByte(parser, $"$100\r\n{payload}\r\n"));

        Assert.Equal(RespType.BulkString, value.Type);
        Assert.True(value.Truncated);
        Assert.Equal(100, value.DeclaredLength);
        Assert.Equal(4, value.Bytes!.Length);
        Assert.True(value.HasResult());
        Assert.Equal("xxxx …[bulk string truncated: 100 bytes on the wire, 4 kept]", value.AsText());
        Assert.Equal(value.AsText(), value.Render());
        Assert.Equal(1, parser.OversizePayloadsSkipped);
        Assert.Equal(96, parser.BytesSkipped);
        Assert.Equal(100, parser.LargestOversizePayload);
        Assert.Equal((100L, 4), reported);
        Assert.True(parser.IsAtValueBoundary);
    }

    [Fact]
    public void AnOversizeBulkInsideANestedAggregateIsTruncatedInPlace()
    {
        var big = new string('y', 50);
        var wire = $"*3\r\n$1\r\na\r\n*2\r\n$50\r\n{big}\r\n:7\r\n$1\r\nb\r\n";
        var parser = Parser(maxBulk: 8, preview: 3);

        var value = Assert.Single(Feed(parser, Encoding.UTF8.GetBytes(wire), RandomChunks(new Random(7), wire.Length).ToList()));

        Assert.Equal(RespType.Array, value.Type);
        Assert.Equal(3, value.Items!.Count);
        var inner = value.Items[1];
        Assert.Equal(RespType.Array, inner.Type);
        Assert.True(inner.Items![0].Truncated);
        Assert.Equal(50, inner.Items[0].DeclaredLength);
        Assert.Equal("[a, [yyy …[bulk string truncated: 50 bytes on the wire, 3 kept], 7], b]", value.Render());
        Assert.True(value.HasResult());
        Assert.Equal(1, parser.OversizePayloadsSkipped);
        Assert.Equal(47, parser.BytesSkipped);
    }

    [Fact]
    public void ATenMebibytePayloadInSocketSizedChunksKeepsOnlyThePreview()
    {
        const int size = 10 * 1024 * 1024;
        var header = Encoding.UTF8.GetBytes($"${size}\r\n");
        var wire = new byte[header.Length + size + 2];
        header.CopyTo(wire, 0);
        Array.Fill(wire, (byte)'z', header.Length, size);
        wire[^2] = (byte)'\r';
        wire[^1] = (byte)'\n';

        var parser = Parser(maxBulk: 65536, preview: 65408);
        var value = Assert.Single(Feed(parser, wire, Enumerable.Repeat(32 * 1024, wire.Length / (32 * 1024) + 1)));

        Assert.True(value.Truncated);
        Assert.Equal(size, value.DeclaredLength);
        Assert.Equal(65408, value.Bytes!.Length);
        Assert.Equal(size - 65408, parser.BytesSkipped);
        Assert.EndsWith(" …[bulk string truncated: 10,485,760 bytes on the wire, 65,408 kept]", value.AsText());
    }

    [Fact]
    public void ThePreviewIsClampedToTheBulkCapAndMayBeEmpty()
    {
        var clamped = Assert.Single(FeedWhole(Parser(maxBulk: 5, preview: 100), "$20\r\nabcdefghijklmnopqrst\r\n"));
        Assert.Equal(5, clamped.Bytes!.Length);
        Assert.Equal("abcde …[bulk string truncated: 20 bytes on the wire, 5 kept]", clamped.AsText());

        var empty = Assert.Single(FeedWhole(Parser(maxBulk: 5, preview: 0), "$20\r\nabcdefghijklmnopqrst\r\n"));
        Assert.Empty(empty.Bytes!);
        Assert.Equal(" …[bulk string truncated: 20 bytes on the wire, 0 kept]", empty.AsText());
    }

    [Fact]
    public void APayloadExactlyAtTheCapIsReadInFull()
    {
        var value = Assert.Single(FeedWhole(Parser(maxBulk: 5, preview: 2), "$5\r\nhello\r\n"));
        Assert.False(value.Truncated);
        Assert.Equal("hello", value.AsText());
        Assert.Equal(5, value.DeclaredLength);
    }

    [Fact]
    public void VerbatimStringsAndBlobErrorsAreStreamedPastToo()
    {
        var verbatim = Assert.Single(FeedWhole(Parser(maxBulk: 4, preview: 4), "=15\r\ntxt:Some string\r\n"));
        Assert.Equal(RespType.VerbatimString, verbatim.Type);
        Assert.True(verbatim.Truncated);
        Assert.StartsWith("txt:", verbatim.AsText());

        var error = Assert.Single(FeedWhole(Parser(maxBulk: 4, preview: 4), "!21\r\nSYNTAX invalid syntax\r\n"));
        Assert.Equal(RespType.BlobError, error.Type);
        Assert.True(error.IsError);
        Assert.False(error.HasResult());
    }

    // ---- segmentation edge cases ------------------------------------------------------------------

    [Theory]
    [InlineData("+OK\r", "\n")]
    [InlineData("$3\r\nabc\r", "\n")]
    [InlineData("$3\r", "\nabc\r\n")]
    [InlineData("$", "3\r\nabc\r\n")]
    [InlineData("*2\r\n$1\r\na\r", "\n$1\r\nb\r\n")]
    public void ACrLfSplitAcrossSegmentsIsReassembled(string first, string second)
    {
        var parser = Parser();
        var values = new List<RespValue>();
        parser.Feed(Encoding.UTF8.GetBytes(first), values.Add);
        Assert.Empty(values);
        Assert.False(parser.IsAtValueBoundary);
        parser.Feed(Encoding.UTF8.GetBytes(second), values.Add);
        Assert.Single(values);
        Assert.True(parser.IsAtValueBoundary);
    }

    [Fact]
    public void ASegmentMayHoldSeveralValuesAndTheTailOfTheNext()
    {
        var parser = Parser();
        var values = new List<RespValue>();
        parser.Feed("+OK\r\n:1\r\n$3\r\nab"u8, values.Add);
        Assert.Equal(2, values.Count);
        parser.Feed("c\r\n"u8, values.Add);
        Assert.Equal(3, values.Count);
        Assert.Equal("abc", values[2].AsText());
        Assert.Equal(3, parser.ValuesCompleted);
    }

    // ---- inline commands --------------------------------------------------------------------------

    [Fact]
    public void AnInlineCommandIsAnArrayOfItsWords()
    {
        var parser = Parser(inline: true);
        var value = Assert.Single(FeedByteByByte(parser, "SET k v\r\n"));
        Assert.Equal(RespType.Array, value.Type);
        Assert.Equal(["SET", "k", "v"], value.Items!.Select(i => i.AsText()).ToArray());
    }

    [Fact]
    public void AnEmptyInlineLineIsNothingAndANonMarkerLineOnTheReplySideIsAnError()
    {
        Assert.Empty(FeedWhole(Parser(inline: true), "\r\n"));
        Assert.Single(FeedWhole(Parser(inline: true), "\r\nPING\r\n"));
        Assert.Throws<RespProtocolException>(() => FeedWhole(Parser(inline: false), "this is not RESP\r\n"));
    }

    // ---- reset and protocol errors ----------------------------------------------------------------

    [Fact]
    public void ResetMidValueDropsThePartialValueAndTheNextByteStartsAFreshOne()
    {
        var parser = Parser(maxBulk: 4, preview: 2);
        var values = new List<RespValue>();
        parser.Feed("*3\r\n$10\r\nabc"u8, values.Add);
        Assert.False(parser.IsAtValueBoundary);
        Assert.Equal(1, parser.Depth);

        parser.Reset();
        Assert.True(parser.IsAtValueBoundary);
        Assert.Equal(0, parser.Depth);
        Assert.Equal(0, parser.HeldBytes);

        parser.Feed("+OK\r\n"u8, values.Add);
        Assert.Equal("OK", Assert.Single(values).AsText());
    }

    [Fact]
    public void HoldingMoreThanTheCapWithoutCompletingAValueIsARecoverableError()
    {
        var parser = Parser(maxBulk: 10, preview: 10, maxHeld: 100);
        var values = new List<RespValue>();
        // An aggregate count that was really payload: the elements never add up to a value.
        var ex = Assert.Throws<TapProtocolException>(() =>
        {
            parser.Feed("*1000\r\n"u8, values.Add);
            for (var i = 0; i < 100; i++)
                parser.Feed(":1\r\n"u8, values.Add);
        });
        Assert.True(ex.Recoverable);
        Assert.Contains("MaxBufferedBytes", ex.Message);
        Assert.Empty(values);
    }

    [Fact]
    public void ASubCapBulkNeverTripsTheHeldBytesCap()
    {
        var parser = Parser(maxBulk: 1000, preview: 10, maxHeld: 1000);
        var value = Assert.Single(FeedWhole(parser, $"$1000\r\n{new string('q', 1000)}\r\n"));
        Assert.False(value.Truncated);
    }

    [Fact]
    public void AnOversizePayloadNeverCountsAgainstTheHeldBytesCap()
    {
        var parser = Parser(maxBulk: 10, preview: 10, maxHeld: 50);
        var value = Assert.Single(FeedWhole(parser, $"$1000\r\n{new string('q', 1000)}\r\n"));
        Assert.True(value.Truncated);
        Assert.Equal(0, parser.HeldBytes);
    }

    [Theory]
    [InlineData("?nope\r\n")]
    [InlineData("$abc\r\nx\r\n")]
    [InlineData(":x\r\n")]
    [InlineData("*abc\r\n")]
    [InlineData("+OK\n")]
    [InlineData("$99999999999999\r\n")]
    public void BytesThatAreNotRespAreANonRecoverableProtocolError(string wire)
    {
        var ex = Assert.Throws<RespProtocolException>(() => FeedWhole(Parser(), wire));
        Assert.False(ex.Recoverable);
    }

    [Fact]
    public void AHeaderWithNoLineEndWithinSixtyFourBytesIsAProtocolError()
    {
        var parser = Parser();
        Assert.Throws<RespProtocolException>(() => FeedWhole(parser, "$" + new string('1', 80)));
    }

    [Fact]
    public void ASimpleStringMayBeLongerThanSixtyFourBytes()
    {
        var text = new string('s', 300);
        var value = Assert.Single(FeedByteByByte(Parser(), $"+{text}\r\n"));
        Assert.Equal(text, value.AsText());
    }

    [Fact]
    public void CountersSurviveAReset()
    {
        var parser = Parser(maxBulk: 2, preview: 1);
        FeedWhole(parser, "$5\r\nhello\r\n");
        parser.Feed("$5\r\nhe"u8, _ => { });
        parser.Reset();
        Assert.Equal(1, parser.OversizePayloadsSkipped);
        Assert.Equal(5, parser.BytesSkipped); // 4 from the completed payload + 1 from the abandoned one
        Assert.Equal(1, parser.ValuesCompleted);
    }
}
