using System.Text;
using Kronikol.Extensions.TcpTap.Protocols;

namespace Kronikol.Tests.TcpTap;

public class RespParserTests
{
    private static RespValue Parse(string wire)
    {
        Assert.True(RespParser.TryParse(Encoding.UTF8.GetBytes(wire), out var value, out var consumed));
        Assert.Equal(Encoding.UTF8.GetByteCount(wire), consumed);
        return value!;
    }

    [Fact]
    public void SimpleStringParses()
    {
        var value = Parse("+OK\r\n");
        Assert.Equal(RespType.SimpleString, value.Type);
        Assert.Equal("OK", value.AsText());
    }

    [Fact]
    public void ErrorParsesAndIsFlagged()
    {
        var value = Parse("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n");
        Assert.Equal(RespType.Error, value.Type);
        Assert.True(value.IsError);
        Assert.False(value.HasResult());
        Assert.StartsWith("WRONGTYPE", value.AsText());
    }

    [Fact]
    public void IntegerParses()
    {
        var value = Parse(":42\r\n");
        Assert.Equal(RespType.Integer, value.Type);
        Assert.Equal(42, value.Integer);
        Assert.Equal("42", value.AsText());
    }

    [Fact]
    public void NegativeIntegerParses() => Assert.Equal(-7, Parse(":-7\r\n").Integer);

    [Fact]
    public void BulkStringKeepsItsBytes()
    {
        var value = Parse("$5\r\nhello\r\n");
        Assert.Equal(RespType.BulkString, value.Type);
        Assert.Equal("hello", value.AsText());
        Assert.True(value.HasResult());
    }

    [Fact]
    public void EmptyBulkStringIsNotNull()
    {
        var value = Parse("$0\r\n\r\n");
        Assert.Equal(RespType.BulkString, value.Type);
        Assert.Equal("", value.AsText());
    }

    [Fact]
    public void BulkStringMayContainCrLf()
    {
        var value = Parse("$5\r\na\r\nb!\r\n");
        Assert.Equal("a\r\nb!", value.AsText());
    }

    [Fact]
    public void NullBulkStringIsAMiss()
    {
        var value = Parse("$-1\r\n");
        Assert.Equal(RespType.Null, value.Type);
        Assert.True(value.IsNull);
        Assert.False(value.HasResult());
        Assert.Null(value.AsText());
    }

    [Fact]
    public void NullArrayIsNull() => Assert.True(Parse("*-1\r\n").IsNull);

    [Fact]
    public void ArrayParsesItsElements()
    {
        var value = Parse("*3\r\n$1\r\na\r\n:2\r\n+three\r\n");
        Assert.Equal(RespType.Array, value.Type);
        Assert.Equal(3, value.Items!.Count);
        Assert.Equal("a", value.Items[0].AsText());
        Assert.Equal("2", value.Items[1].AsText());
        Assert.Equal("three", value.Items[2].AsText());
        Assert.Equal("[a, 2, three]", value.Render());
    }

    [Fact]
    public void EmptyArrayHasNoResult()
    {
        var value = Parse("*0\r\n");
        Assert.Equal(RespType.Array, value.Type);
        Assert.Empty(value.Items!);
        Assert.False(value.HasResult());
    }

    [Fact]
    public void NestedArraysParse()
    {
        var value = Parse("*2\r\n*2\r\n$1\r\na\r\n$1\r\nb\r\n*1\r\n:9\r\n");
        Assert.Equal(2, value.Items!.Count);
        Assert.Equal(RespType.Array, value.Items[0].Type);
        Assert.Equal("[[a, b], [9]]", value.Render());
    }

    [Fact]
    public void ArrayWithNilElementsCountsAsAHitWhenAnyElementIsPresent()
    {
        var value = Parse("*2\r\n$-1\r\n$1\r\nb\r\n");
        Assert.True(value.HasResult());
        Assert.Equal("[(nil), b]", value.Render());
    }

    [Fact]
    public void ArrayOfOnlyNilsIsAMiss()
    {
        var value = Parse("*2\r\n$-1\r\n$-1\r\n");
        Assert.False(value.HasResult());
    }

    // ---- RESP3 ------------------------------------------------------------------------------------

    [Fact]
    public void Resp3NullParses() => Assert.True(Parse("_\r\n").IsNull);

    [Fact]
    public void Resp3DoubleParses()
    {
        var value = Parse(",1.23\r\n");
        Assert.Equal(RespType.Double, value.Type);
        Assert.Equal("1.23", value.AsText());
    }

    [Theory]
    [InlineData("#t\r\n", "true")]
    [InlineData("#f\r\n", "false")]
    public void Resp3BooleanParses(string wire, string expected)
    {
        var value = Parse(wire);
        Assert.Equal(RespType.Boolean, value.Type);
        Assert.Equal(expected, value.AsText());
    }

    [Fact]
    public void Resp3BigNumberParses()
    {
        var value = Parse("(3492890328409238509324850943850943825024385\r\n");
        Assert.Equal(RespType.BigNumber, value.Type);
    }

    [Fact]
    public void Resp3VerbatimStringParses()
    {
        var value = Parse("=15\r\ntxt:Some string\r\n");
        Assert.Equal(RespType.VerbatimString, value.Type);
        Assert.Equal("txt:Some string", value.AsText());
    }

    [Fact]
    public void Resp3BlobErrorParsesAsAnError()
    {
        var value = Parse("!21\r\nSYNTAX invalid syntax\r\n");
        Assert.Equal(RespType.BlobError, value.Type);
        Assert.True(value.IsError);
    }

    [Fact]
    public void Resp3MapParsesAsFlattenedPairs()
    {
        var value = Parse("%2\r\n$5\r\nfirst\r\n:1\r\n$6\r\nsecond\r\n:2\r\n");
        Assert.Equal(RespType.Map, value.Type);
        Assert.Equal(4, value.Items!.Count);
        Assert.Equal("{first=1, second=2}", value.Render());
    }

    [Fact]
    public void Resp3SetParses()
    {
        var value = Parse("~2\r\n$1\r\na\r\n$1\r\nb\r\n");
        Assert.Equal(RespType.Set, value.Type);
        Assert.Equal(2, value.Items!.Count);
    }

    [Fact]
    public void Resp3PushParses()
    {
        var value = Parse(">3\r\n$7\r\nmessage\r\n$3\r\nfoo\r\n$3\r\nbar\r\n");
        Assert.Equal(RespType.Push, value.Type);
        Assert.Equal(3, value.Items!.Count);
    }

    // ---- framing ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("+OK")]
    [InlineData("+OK\r")]
    [InlineData("$5\r\nhel")]
    [InlineData("$5\r\nhello\r")]
    [InlineData("*2\r\n$1\r\na\r\n")]
    public void IncompleteInputAsksForMoreBytes(string wire)
    {
        Assert.False(RespParser.TryParse(Encoding.UTF8.GetBytes(wire), out var value, out var consumed));
        Assert.Null(value);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void OnlyTheFirstValueIsConsumedFromAPipelinedBuffer()
    {
        var wire = Encoding.UTF8.GetBytes("+OK\r\n:1\r\n$3\r\nabc\r\n");
        Assert.True(RespParser.TryParse(wire, out var first, out var consumed));
        Assert.Equal("OK", first!.AsText());
        Assert.Equal(5, consumed);

        Assert.True(RespParser.TryParse(wire.AsSpan(consumed), out var second, out var secondConsumed));
        Assert.Equal(1, second!.Integer);
        Assert.Equal(4, secondConsumed);
    }

    [Fact]
    public void AnUnknownTypeMarkerIsAProtocolError() =>
        Assert.Throws<RespProtocolException>(() => RespParser.TryParse("?nope\r\n"u8, out _, out _));

    [Fact]
    public void AMalformedBulkLengthIsAProtocolError() =>
        Assert.Throws<RespProtocolException>(() => RespParser.TryParse("$abc\r\nx\r\n"u8, out _, out _));

    // ---- commands ---------------------------------------------------------------------------------

    [Fact]
    public void ACommandIsAnArrayOfBulkStrings()
    {
        Assert.True(RespParser.TryParseCommand(Encoding.UTF8.GetBytes(Resp.Command("SET", "k", "v")), out var arguments, out _));
        Assert.Equal(["SET", "k", "v"], arguments!);
    }

    [Fact]
    public void AnInlineCommandIsAccepted()
    {
        Assert.True(RespParser.TryParseCommand("PING\r\n"u8, out var arguments, out var consumed));
        Assert.Equal(["PING"], arguments!);
        Assert.Equal(6, consumed);
    }

    [Fact]
    public void AnIncompleteCommandAsksForMoreBytes() =>
        Assert.False(RespParser.TryParseCommand(Encoding.UTF8.GetBytes("*2\r\n$3\r\nGET\r\n$3\r\nfo"), out _, out _));
}
